using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.ServiceDefaults;
using Microsoft.Extensions.Logging;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cerbos;

// Adapter over an out-of-process Cerbos full-decision PDP. Cerbos, like OPA, owns the ENTIRE fintech
// decision natively — coarse scopes, role eligibility, the 10,000 maker-checker threshold, tenant
// isolation, segregation of duties, and the threshold obligation — expressed as declarative YAML
// (CEL) policies in infra/cerbos/policies. This provider forwards each AccessRequest to Cerbos (via
// the ICerbosCheckClient seam) and maps the engine's EFFECT_ALLOW/EFFECT_DENY + the matching rule's
// output token back onto the shared AccessDecision, so the Cerbos policy answers the SAME question
// shape as the in-process reference engine (the head-to-head with OPA, over gRPC instead of REST).
//
// The IAuthorizationDecisionProvider contract is synchronous; the Cerbos SDK's CheckResources is
// synchronous too, so — like the OPA adapter — this returns synchronously with no async bridging.
//
// Fail-closed posture: any failure to obtain a well-formed Permit/Deny from Cerbos — a not-configured
// or unreachable server, a gRPC error, an absent result entry, a missing/unknown output token, an
// unrecognized reason code, or a decision/reason inconsistency — returns Deny with the provider-local
// ProviderUnavailable reason. It never falls through to a permit and never throws through to the
// (anonymous) /api/authz/evaluate caller. The specific cause is LOGGED (sanitized — CWE-117); the
// returned decision carries only a stable, non-sensitive message.
public sealed class CerbosDecisionProvider : IAuthorizationDecisionProvider
{
    public const string EngineName = "cerbos";

    // Provider-local reason for an unreachable/misbehaving engine. Deliberately NOT part of the shared
    // ReasonCodes: it maps to no Bank.Api rule and never appears in the parity catalog (Cerbos is
    // reachable and its policy is total there). It exists only so a real outage is a legible,
    // machine-stable Deny rather than an opaque 500. Mirrors the OPA adapter's ProviderUnavailable.
    private const string ProviderUnavailable = "ProviderUnavailable";

    // Stable, non-sensitive message returned to callers on every fail-closed decision; the specific
    // cause is logged instead of surfaced, so /api/authz/evaluate cannot leak internal config/network
    // detail to anonymous callers.
    private const string ProviderUnavailableMessage =
        "The Cerbos authorization engine did not return a usable decision; failing closed.";

    // The Cerbos resource-policy id the adapter queries, surfaced (CS16) as a stable engine-native
    // PolicyReference so an audit/playground explorer can locate the deciding policy.
    private const string ResourcePolicyId = "resource.bank.vdefault";

    // Human-readable detail naming the Cerbos resource policy that owns the determining rule.
    private const string ResourcePolicyDetail = "Cerbos resource policy 'bank' (version default)";

    // The bounded reason vocabulary the adapter accepts from Cerbos. Cerbos is out-of-process and its
    // policy could emit an arbitrary output token; anything outside this set fails closed so an
    // unknown or attacker-influenced code cannot reach callers or inflate audit/metric cardinality.
    // Mirrors the shared ReasonCodes (incl. the CS11 governance SodConflict verdict) so a governance
    // SoD denial surfaces its real reason through the Cerbos path instead of degrading to
    // ProviderUnavailable.
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

    private readonly ICerbosCheckClient _client;
    private readonly ILogger<CerbosDecisionProvider> _logger;

    public CerbosDecisionProvider(ICerbosCheckClient client, ILogger<CerbosDecisionProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string Name => EngineName;

    public AccessDecision Evaluate(AccessRequest request)
    {
        // The Cerbos "bank" policy models the BASE fintech decision only. On-behalf-of (Subject.Actor),
        // manager->delegate delegation (Context.Delegation), and break-glass elevation (Context.BreakGlass)
        // are CS19/CS21 constraints the policy does NOT encode. Forwarding such a request would let Cerbos
        // permit an OBO call the reference engine DENIES (e.g. an actor missing the delegated scope) — a
        // fail-OPEN. Until the policy supports them, any request carrying delegation/OBO/break-glass
        // context fails closed (documented boundary in docs/authz/cerbos-adapter.md), never a permit.
        if (request.Subject.Actor is not null
            || request.Context.Delegation is not null
            || request.Context.BreakGlass is not null)
        {
            return FailClosed(
                "Cerbos adapter does not evaluate on-behalf-of / delegation / break-glass requests; " +
                "failing closed.",
                request);
        }

        try
        {
            return MapOutcome(_client.Check(request), request);
        }
        catch (Exception ex)
        {
            // An authorization PDP must never throw through to the caller: a not-configured,
            // unreachable, or otherwise-failing Cerbos engine DENIES rather than surfacing a raw 500.
            return FailClosed($"Cerbos evaluation failed ({ex.GetType().Name}): {ex.Message}", request);
        }
    }

    private AccessDecision MapOutcome(CerbosCheckOutcome outcome, AccessRequest request)
    {
        // No output token = Cerbos' default deny for an action with no matching rule. That is expected
        // ONLY for a genuinely UNKNOWN action (outside the known vocabulary) → UnknownAction. A KNOWN
        // action that produced no output means a malformed policy/server response → fail closed (not a
        // misleading UnknownAction). A permit with no output means a misbehaving policy: fail closed.
        if (outcome.OutputToken is null)
        {
            if (outcome.Allowed)
            {
                return FailClosed("Cerbos permitted an action but emitted no policy output token.", request);
            }

            return ActionNames.ForMetric(request.Action.Name) == ActionNames.Unknown
                ? UnknownActionDeny(request)
                : FailClosed("Cerbos denied a known action but emitted no policy output token.", request);
        }

        // Token = "<Reason>" or "Permit:<obligation>"; split once so a reason code that itself
        // contained a colon (none do) would keep the tail intact.
        var separator = outcome.OutputToken.IndexOf(':');
        var reasonCode = separator < 0 ? outcome.OutputToken : outcome.OutputToken[..separator];
        var obligationToken = separator < 0 ? null : outcome.OutputToken[(separator + 1)..];

        if (!KnownReasonCodes.Contains(reasonCode))
        {
            return FailClosed($"Cerbos returned an unrecognized reason code '{reasonCode}'.", request);
        }

        var reason = new Reason(reasonCode, $"Cerbos policy decision: {reasonCode}.");

        // Enforce decision/reason consistency (a Permit always carries the Permit reason; a Deny never
        // does), matching the reference engine — a mismatch means a misbehaving policy and fails closed
        // rather than surfacing an incoherent decision.
        return (outcome.Allowed, reasonCode) switch
        {
            (true, ReasonCodes.Permit) =>
                TryMapObligations(obligationToken, out var obligations)
                    ? AccessDecision.Permit(reason, obligations)
                        .WithExplanation(BuildExplanation(request, reason))
                    : FailClosed(
                        $"Cerbos permitted with an unknown obligation token '{obligationToken}'.", request),
            (true, _) =>
                FailClosed($"Cerbos permitted with a non-permit reason code '{reasonCode}'.", request),
            (false, ReasonCodes.Permit) =>
                FailClosed("Cerbos denied with the 'Permit' reason code.", request),
            (false, _) =>
                AccessDecision.Deny(reason).WithExplanation(BuildExplanation(request, reason)),
        };
    }

    private AccessDecision UnknownActionDeny(AccessRequest request)
    {
        var reason = new Reason(
            ReasonCodes.UnknownAction, $"Cerbos policy decision: {ReasonCodes.UnknownAction}.");
        return AccessDecision.Deny(reason).WithExplanation(BuildExplanation(request, reason));
    }

    // Builds the engine-native explanation (CS16) for a well-formed Cerbos decision. It normalizes the
    // determining rule from the reason code and surfaces the Cerbos-native artifacts: the determining
    // check id ("<action-short>.<Reason>", mirroring the OPA rule ids so explanations compare across
    // engines) and the stable resource-policy id. The contract's PolicyReferenceKinds has no
    // Cerbos-specific kind (adding one is a Contracts change, out of this adapter's scope), so both
    // references use the generic normalized `Rule` kind.
    private static DecisionExplanation BuildExplanation(AccessRequest request, Reason reason) =>
        new(
            Engine: EngineName,
            DeterminingRule: DecisionExplanations.RuleForReason(reason.Code),
            PolicyReferences:
            [
                new PolicyReference(
                    PolicyReferenceKinds.Rule,
                    $"{ActionShort(request.Action.Name)}.{reason.Code}",
                    ResourcePolicyDetail),
                new PolicyReference(PolicyReferenceKinds.Rule, ResourcePolicyId),
            ],
            Narrative: reason.Message);

    // The short action label used in the determining-rule id, mirroring the OPA policy's naming so the
    // Cerbos and OPA explanations line up in the playground.
    private static string ActionShort(string action) => action switch
    {
        ActionNames.AccountRead => "read",
        ActionNames.AccountCreate => "account.create",
        ActionNames.TransactionCreate => "transaction.create",
        ActionNames.TransactionApprove or ActionNames.TransactionReject => "approval",
        ActionNames.GovernanceAccessRequest => "governance.access.request",
        _ => "unknown",
    };

    // Maps the Permit obligation suffix to obligations, returning false (→ fail closed) for a NON-NULL
    // unknown/empty token. A malformed obligation (e.g. a typo like "requre_approval") must never permit
    // while silently dropping the maker-checker approval requirement — that would be a fail-OPEN on the
    // 10,000 threshold. A null token = a permit that legitimately carries no obligation (e.g. a read or
    // a below-threshold transaction).
    private static bool TryMapObligations(string? obligationToken, out Obligation[] obligations)
    {
        switch (obligationToken)
        {
            case null:
                obligations = [];
                return true;
            case "require_approval":
                obligations = [new Obligation(ObligationIds.RequireApproval)];
                return true;
            case "post_immediately":
                obligations = [new Obligation(ObligationIds.PostImmediately)];
                return true;
            default:
                obligations = [];
                return false;
        }
    }

    // Log the specific cause for operators/telemetry (sanitized — CWE-117); return only the stable,
    // non-sensitive message to the caller so no internal detail leaks through the anonymous evaluate
    // endpoint.
    private AccessDecision FailClosed(string diagnostic, AccessRequest request)
    {
        _logger.LogWarning(
            "Cerbos adapter failing closed for action={Action}: {Diagnostic}",
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
