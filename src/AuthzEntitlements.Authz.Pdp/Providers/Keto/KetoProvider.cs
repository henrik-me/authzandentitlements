using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using AuthzEntitlements.ServiceDefaults;
using Microsoft.Extensions.Logging;

namespace AuthzEntitlements.Authz.Pdp.Providers.Keto;

// The Ory Keto (ReBAC / Zanzibar) engine behind the CS05 provider seam. Selected by
// "Pdp:Provider" = "keto"; registered alongside the reference engine and never on the default path.
// Evaluate maps an AuthZEN AccessRequest to a single Keto permission check (subject -> permission ->
// resource) and returns a self-explaining Permit/Deny.
//
// This is the third ReBAC counterpart alongside SpiceDbProvider and OpenFgaProvider: all three answer
// the SAME account-shaped relationship questions over the SAME seed graph (RebacSeedTuples), so the
// engines must agree scenario-for-scenario. Keto reaches its server over an HTTP REST API split
// across a read port (checks) and a write port (relationship mutations) where SpiceDB uses gRPC and
// OpenFGA its REST SDK; the adapter shape is otherwise identical.
//
// Evaluate is synchronous per IAuthorizationDecisionProvider; the Keto client is async, so it bridges
// with GetAwaiter().GetResult() — the interface explicitly allows an out-of-process adapter to compute
// asynchronously and return the result here.
//
// Fail-closed posture: any failure to obtain a check result — Keto not configured (blank endpoints),
// unreachable, an HTTP error, or an unknown result — returns Deny, never a permit or an unhandled
// throw. An authorization PDP must not surface a raw 500 through /api/authz/evaluate; the cause is
// logged and callers get only a stable, non-sensitive reason/message (same posture as the SpiceDB,
// OpenFGA, and OPA adapters).
public sealed class KetoProvider : IAuthorizationDecisionProvider
{
    // The registered provider name (Pdp:Provider=keto), shared with the mapper's explanations.
    public const string EngineName = "keto";

    // Stable, non-sensitive message returned to (anonymous) /api/authz/evaluate callers on a
    // fail-closed decision; the specific cause is logged, never surfaced.
    private const string EngineUnavailableMessage =
        "The Keto authorization engine could not be reached; failing closed (deny).";

    private readonly IKetoCheckClient _service;
    private readonly ILogger<KetoProvider> _logger;

    public KetoProvider(IKetoCheckClient service, ILogger<KetoProvider> logger)
    {
        _service = service;
        _logger = logger;
    }

    public string Name => EngineName;

    public AccessDecision Evaluate(AccessRequest request)
    {
        // Fail closed at the mapper: an unsupported action or a resource with no id never reaches
        // Keto — it is denied with a specific reason.
        if (!KetoRequestMapper.TryMap(request, out var check, out var denial))
        {
            return denial;
        }

        // The checked relationship, surfaced in the explanation as the ReBAC "relationship path"
        // (CS16): "user:...#permission@account:...". The same tuple shape SpiceDB and OpenFGA surface,
        // so all three engines' explanations line up in the playground.
        var tuple = $"{RebacTypes.User}:{check.SubjectId}#{check.Permission}@{RebacTypes.Account}:{check.AccountId}";

        try
        {
            var allowed = _service
                .CheckAsync(check.SubjectId, check.Permission, check.AccountId)
                .GetAwaiter()
                .GetResult();

            if (allowed)
            {
                var permitNarrative =
                    $"Keto grants '{check.Permission}' on 'account:{check.AccountId}' to 'user:{check.SubjectId}'.";
                return AccessDecision.Permit(new Reason(ReasonCodes.Permit, permitNarrative))
                    .WithExplanation(new DecisionExplanation(
                        Engine: EngineName,
                        DeterminingRule: DeterminingRules.Relationship,
                        PolicyReferences: [new PolicyReference(
                            PolicyReferenceKinds.RelationshipTuple,
                            tuple,
                            Detail: "A relationship path grants this permission.")],
                        Narrative: permitNarrative));
            }

            var denyNarrative =
                $"Keto finds no relationship granting '{check.Permission}' on 'account:{check.AccountId}' to 'user:{check.SubjectId}'.";
            return AccessDecision.Deny(new Reason(RebacReasonCodes.NoRelationship, denyNarrative))
                .WithExplanation(new DecisionExplanation(
                    Engine: EngineName,
                    DeterminingRule: DeterminingRules.Relationship,
                    PolicyReferences: [new PolicyReference(
                        PolicyReferenceKinds.RelationshipTuple,
                        tuple,
                        Detail: "No relationship path grants this permission.")],
                    Narrative: denyNarrative));
        }
        catch (Exception ex)
        {
            // An authorization PDP must never throw through to the caller: a not-configured,
            // unreachable, or otherwise-failing Keto engine DENIES rather than surfacing a raw 500 from
            // /api/authz/evaluate. The cause is logged (sanitized — CWE-117); callers get only the
            // stable message above.
            _logger.LogWarning(
                ex,
                "Keto Check failed for subject={Subject} permission={Permission} account={Account}; failing closed (deny).",
                LogSanitizer.Clean(check.SubjectId),
                LogSanitizer.Clean(check.Permission),
                LogSanitizer.Clean(check.AccountId));
            return AccessDecision.Deny(new Reason(RebacReasonCodes.EngineUnavailable, EngineUnavailableMessage))
                .WithExplanation(new DecisionExplanation(
                    Engine: EngineName,
                    DeterminingRule: DeterminingRules.EngineUnavailable,
                    PolicyReferences: [new PolicyReference(
                        PolicyReferenceKinds.ReasonCode,
                        RebacReasonCodes.EngineUnavailable)],
                    Narrative: EngineUnavailableMessage));
        }
    }
}
