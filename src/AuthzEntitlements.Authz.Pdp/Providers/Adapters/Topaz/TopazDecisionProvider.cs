using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.ServiceDefaults;
using Microsoft.Extensions.Logging;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Topaz;

// Adapter over an out-of-process Topaz (Aserto) authorizer, driven as a FULL-DECISION engine. Topaz is
// OPA-based: it evaluates an OPA policy bundle behind the Aserto authorizer API. This provider feeds it
// the SAME Rego the OPA adapter uses (infra/opa/policy) via the ITopazCheckClient seam — querying
// data.authz.bank.decision with the AccessRequest as `input` — and maps the returned Rego decision
// object ({decision, reason, rule, obligations}) back onto the shared AccessDecision. So the OPA policy
// running INSIDE Topaz answers the SAME question shape as the in-process reference engine and the
// standalone OPA adapter: the head-to-head "OPA standalone vs OPA-inside-Topaz".
//
// Parity boundary: Topaz's Zanzibar directory / ReBAC path is deliberately NOT consulted for the
// decision (like the SpiceDB adapter documents its ReBAC boundary). The whole fintech verdict comes
// from the OPA bundle + request input, so Topaz reproduces the reference decision AND reason code
// exactly. See docs/authz/topaz-adapter.md.
//
// The IAuthorizationDecisionProvider contract is synchronous; the Aserto authorizer client is async, so
// the TopazCheckService seam bridges internally (GetAwaiter().GetResult()) — this provider just calls
// the sync seam.
//
// Fail-closed posture: any failure to obtain a well-formed Permit/Deny from Topaz — a not-configured or
// unreachable authorizer, a gRPC error, an empty/malformed query result, a missing/unknown reason code,
// a decision/reason inconsistency, or an unknown obligation on a permit — returns Deny with the
// provider-local ProviderUnavailable reason. It never falls through to a permit and never throws through
// to the (anonymous) /api/authz/evaluate caller. The specific cause is LOGGED (sanitized — CWE-117); the
// returned decision carries only a stable, non-sensitive message.
public sealed class TopazDecisionProvider : IAuthorizationDecisionProvider
{
    public const string EngineName = "topaz";

    // Provider-local reason for an unreachable/misbehaving engine. Deliberately NOT part of the shared
    // ReasonCodes: it maps to no Bank.Api rule and never appears in the parity catalog (Topaz is
    // reachable and its OPA bundle is total there). It exists only so a real outage is a legible,
    // machine-stable Deny rather than an opaque 500. Mirrors the OPA/Cerbos adapters' ProviderUnavailable.
    private const string ProviderUnavailable = "ProviderUnavailable";

    // Stable, non-sensitive message returned to callers on every fail-closed decision; the specific
    // cause is logged instead of surfaced, so /api/authz/evaluate cannot leak internal config/network
    // detail to anonymous callers.
    private const string ProviderUnavailableMessage =
        "The Topaz authorization engine did not return a usable decision; failing closed.";

    // The Rego package-path the adapter queries (the Topaz OPA bundle's decision rule). Surfaced (CS16)
    // as a stable, engine-native PolicyReference on every well-formed explanation so an audit/playground
    // explorer can locate the deciding policy entry point even when the policy predates the per-decision
    // `rule` field. Identical to the OPA adapter's — Topaz runs the SAME Rego.
    private const string DecisionPackagePath = "data.authz.bank.decision";

    // Human-readable detail naming the Rego package that owns the determining rule.
    private const string RegoPackageDetail = "package authz.bank";

    // The bounded reason vocabulary the adapter accepts from Topaz. Topaz is out-of-process and its OPA
    // bundle could return an arbitrary string; anything outside this set fails closed so an unknown or
    // attacker-influenced code cannot reach callers or inflate audit/metric (pdp.reason) cardinality.
    // Mirrors the shared ReasonCodes exactly (incl. the declared-but-unemitted BranchNotInTenant and the
    // CS11 governance SodConflict verdict), so a governance SoD denial surfaces its real reason through
    // the Topaz path instead of degrading to ProviderUnavailable.
    private static readonly HashSet<string> KnownReasonCodes = new(StringComparer.Ordinal)
    {
        ReasonCodes.Permit,
        ReasonCodes.MissingScope,
        ReasonCodes.TenantMismatch,
        ReasonCodes.RoleNotAuthorized,
        ReasonCodes.SubjectNotMaker,
        ReasonCodes.MakerEqualsChecker,
        ReasonCodes.NotPending,
        ReasonCodes.BranchNotInTenant,
        ReasonCodes.SodConflict,
        ReasonCodes.UnknownAction,
    };

    private readonly ITopazCheckClient _client;
    private readonly ILogger<TopazDecisionProvider> _logger;

    public TopazDecisionProvider(ITopazCheckClient client, ILogger<TopazDecisionProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string Name => EngineName;

    public AccessDecision Evaluate(AccessRequest request)
    {
        // On-behalf-of (Subject.Actor), manager->delegate delegation (Context.Delegation), and break-glass
        // elevation (Context.BreakGlass) are CS19/CS21 constraints the shared bank Rego does not encode.
        // The authoritative fail-closed guard lives at the factory seam (CS45 ExtendedContextGuardProvider,
        // wrapped around every provider that does not declare ISupportsExtendedAuthorizationContext), so a
        // request carrying any of those fields is denied BEFORE it reaches this adapter — the adapter does
        // not (and must not) declare that interface. See docs/authz/topaz-adapter.md.
        try
        {
            return MapDecision(_client.Check(request), request);
        }
        catch (Exception ex)
        {
            // An authorization PDP must never throw through to the caller: a not-configured, unreachable,
            // or otherwise-failing Topaz authorizer DENIES rather than surfacing a raw 500.
            return FailClosed($"Topaz evaluation failed ({ex.GetType().Name}): {ex.Message}", request);
        }
    }

    private AccessDecision MapDecision(TopazCheckOutcome outcome, AccessRequest request)
    {
        // Absent decision object = empty query result (the OPA bundle was undefined for the input, or the
        // authorizer returned a structurally malformed response): fail closed, never a silent permit.
        if (outcome == TopazCheckOutcome.None)
        {
            return FailClosed(
                "Topaz returned no decision binding (policy undefined for the input).", request);
        }

        if (string.IsNullOrWhiteSpace(outcome.Reason))
        {
            return FailClosed("Topaz decision object was missing a reason code.", request);
        }

        // Topaz's OPA bundle could return an arbitrary reason string; accept only the bounded ReasonCodes
        // so an unknown code cannot reach callers or inflate audit/metric cardinality.
        if (!KnownReasonCodes.Contains(outcome.Reason))
        {
            return FailClosed($"Topaz returned an unrecognized reason code '{outcome.Reason}'.", request);
        }

        var reason = new Reason(outcome.Reason, $"Topaz policy decision: {outcome.Reason}.");

        // Enforce decision/reason consistency (a Permit always carries the Permit reason; a Deny never
        // does), matching the reference engine — a mismatch means a misbehaving policy and fails closed
        // rather than surfacing an incoherent decision.
        return outcome.Decision switch
        {
            "Permit" when outcome.Reason == ReasonCodes.Permit =>
                MapPermit(outcome, reason, request),
            "Permit" =>
                FailClosed($"Topaz permitted with a non-permit reason code '{outcome.Reason}'.", request),
            "Deny" when outcome.Reason != ReasonCodes.Permit =>
                AccessDecision.Deny(reason).WithExplanation(BuildExplanation(outcome, reason)),
            "Deny" =>
                FailClosed("Topaz denied with the 'Permit' reason code.", request),
            _ =>
                FailClosed($"Topaz returned an unrecognized decision '{outcome.Decision}'.", request),
        };
    }

    // Maps a well-formed Permit (decision "Permit" carrying the "Permit" reason) to a permit decision,
    // failing closed FIRST on a malformed obligations field. A present-but-non-array obligations value
    // (ObligationsMalformed) must never permit while silently dropping an obligation like require_approval
    // — that would be a fail-OPEN on the maker-checker 10,000 threshold. Only once the field is confirmed
    // well-formed (absent/empty/list) are the obligations mapped, and an unknown token in the list still
    // fails closed via TryMapObligations. Deny paths do not consult obligations and never reach here.
    private AccessDecision MapPermit(TopazCheckOutcome outcome, Reason reason, AccessRequest request)
    {
        if (outcome.ObligationsMalformed)
        {
            return FailClosed("Topaz permitted with a malformed (non-array) obligations field.", request);
        }

        return TryMapObligations(outcome.Obligations, out var obligations)
            ? AccessDecision.Permit(reason, obligations).WithExplanation(BuildExplanation(outcome, reason))
            : FailClosed(
                $"Topaz permitted with an unknown obligation in [{FormatObligations(outcome.Obligations)}].",
                request);
    }

    // Builds the engine-native explanation (CS16) for a well-formed Topaz decision. Because Topaz IS OPA
    // under the hood, the explanation mirrors the OPA adapter's EXACTLY so the two line up in the
    // playground: the determining rule is normalized from the reason code, the policy's determining-rule
    // id ("<action-short>.<Reason>") is surfaced first as a Rego rule when present, and the stable
    // package-path reference is ALWAYS present so an older policy that predates the `rule` field still
    // yields a usable explanation — a missing rule degrades the explanation but never fails the decision.
    private static DecisionExplanation BuildExplanation(TopazCheckOutcome outcome, Reason reason)
    {
        var references = new List<PolicyReference>(2);
        if (!string.IsNullOrWhiteSpace(outcome.Rule))
        {
            references.Add(new PolicyReference(
                PolicyReferenceKinds.RegoRule, outcome.Rule, RegoPackageDetail));
        }

        references.Add(new PolicyReference(PolicyReferenceKinds.RegoRule, DecisionPackagePath));

        return new DecisionExplanation(
            Engine: EngineName,
            DeterminingRule: DecisionExplanations.RuleForReason(reason.Code),
            PolicyReferences: references,
            Narrative: reason.Message);
    }

    // Maps the Permit obligation ids to obligations, returning false (→ fail closed) for ANY non-null
    // unknown token. Unlike the standalone OPA adapter (which drops an unrecognized obligation string),
    // Topaz fails closed on one: a malformed obligation (e.g. a typo like "requre_approval") must never
    // permit while silently dropping the maker-checker approval requirement — that would be a fail-OPEN on
    // the 10,000 threshold. An absent/empty obligation list is a permit that legitimately carries no
    // obligation (a read or a below-threshold transaction).
    private static bool TryMapObligations(IReadOnlyList<string>? obligations, out Obligation[] mapped)
    {
        if (obligations is null || obligations.Count == 0)
        {
            mapped = [];
            return true;
        }

        var result = new List<Obligation>(obligations.Count);
        foreach (var id in obligations)
        {
            switch (id)
            {
                case "require_approval":
                    result.Add(new Obligation(ObligationIds.RequireApproval));
                    break;
                case "post_immediately":
                    result.Add(new Obligation(ObligationIds.PostImmediately));
                    break;
                default:
                    mapped = [];
                    return false;
            }
        }

        mapped = result.ToArray();
        return true;
    }

    // Renders the raw obligation tokens for the (logged, sanitized) fail-closed diagnostic when an unknown
    // obligation is present. Never surfaced to callers.
    private static string FormatObligations(IReadOnlyList<string>? obligations) =>
        obligations is null ? string.Empty : string.Join(", ", obligations);

    // Log the specific cause for operators/telemetry (sanitized — CWE-117); return only the stable,
    // non-sensitive message to the caller so no internal detail leaks through the anonymous evaluate
    // endpoint.
    private AccessDecision FailClosed(string diagnostic, AccessRequest request)
    {
        _logger.LogWarning(
            "Topaz adapter failing closed for action={Action}: {Diagnostic}",
            LogSanitizer.Clean(request.Action.Name),
            LogSanitizer.Clean(diagnostic));

        return AccessDecision.Deny(new Reason(ProviderUnavailable, ProviderUnavailableMessage))
            .WithExplanation(new DecisionExplanation(
                Engine: EngineName,
                DeterminingRule: DeterminingRules.EngineUnavailable,
                PolicyReferences: [new PolicyReference(PolicyReferenceKinds.ReasonCode, ProviderUnavailable)],
                Narrative: ProviderUnavailableMessage));
    }
}
