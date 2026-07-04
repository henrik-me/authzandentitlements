using AuthzEntitlements.Authz.Pdp.Contracts;
using Microsoft.Extensions.Logging;

namespace AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

// The OpenFGA (ReBAC / Zanzibar) engine behind the CS05 provider seam. Selected by
// "Pdp:Provider" = "openfga"; registered alongside the reference engine and never on the
// default path. Evaluate maps an AuthZEN AccessRequest to a single OpenFGA forward Check
// (subject -> relation -> object) and returns a self-explaining Permit/Deny. Reverse-index
// queries ("who can view X" / "what can Y access") are OpenFGA-native and exposed via
// OpenFgaRebacService + the RebacEndpoints, beyond this synchronous contract.
//
// Evaluate is synchronous per IAuthorizationDecisionProvider; the SDK is async, so it bridges
// with GetAwaiter().GetResult() — the interface explicitly allows an out-of-process adapter to
// compute asynchronously and return the result here.
//
// Fail-closed posture: any failure to obtain a Check result — OpenFGA not configured (blank
// ApiUrl), unreachable, or any other error — returns Deny, never a permit or an unhandled throw.
// An authorization PDP must not surface a raw 500 through /api/authz/evaluate; the cause is logged
// and callers get only a stable, non-sensitive reason/message (same posture as the OPA adapter).
public sealed class OpenFgaProvider : IAuthorizationDecisionProvider
{
    // Stable, non-sensitive message returned to (anonymous) /api/authz/evaluate callers on a
    // fail-closed decision; the specific cause is logged, never surfaced.
    private const string EngineUnavailableMessage =
        "The OpenFGA authorization engine could not be reached; failing closed (deny).";

    private readonly IOpenFgaCheckClient _service;
    private readonly ILogger<OpenFgaProvider> _logger;

    public OpenFgaProvider(IOpenFgaCheckClient service, ILogger<OpenFgaProvider> logger)
    {
        _service = service;
        _logger = logger;
    }

    public string Name => "openfga";

    public AccessDecision Evaluate(AccessRequest request)
    {
        // Fail closed at the mapper: an unsupported action or a resource with no id never reaches
        // OpenFGA — it is denied with a specific reason.
        if (!OpenFgaRequestMapper.TryMap(request, out var check, out var denial))
        {
            return denial;
        }

        // The checked relationship tuple, surfaced in the explanation as the ReBAC "relationship path"
        // (CS16): "user:...#relation@account:...". A full multi-hop Expand is out of scope; the checked
        // tuple is the offline-testable minimum determining artifact for the Check result.
        var tuple = $"{check.User}#{check.Relation}@{check.Object}";

        try
        {
            var allowed = _service
                .CheckAsync(check.User, check.Relation, check.Object)
                .GetAwaiter()
                .GetResult();

            if (allowed)
            {
                var permitNarrative =
                    $"OpenFGA grants '{check.Relation}' on '{check.Object}' to '{check.User}'.";
                return AccessDecision.Permit(new Reason(ReasonCodes.Permit, permitNarrative))
                    .WithExplanation(new DecisionExplanation(
                        Engine: "openfga",
                        DeterminingRule: DeterminingRules.Relationship,
                        PolicyReferences: [new PolicyReference(
                            PolicyReferenceKinds.RelationshipTuple,
                            tuple,
                            Detail: "A relationship path grants this relation.")],
                        Narrative: permitNarrative));
            }

            var denyNarrative =
                $"OpenFGA finds no relationship granting '{check.Relation}' on '{check.Object}' to '{check.User}'.";
            return AccessDecision.Deny(new Reason(RebacReasonCodes.NoRelationship, denyNarrative))
                .WithExplanation(new DecisionExplanation(
                    Engine: "openfga",
                    DeterminingRule: DeterminingRules.Relationship,
                    PolicyReferences: [new PolicyReference(
                        PolicyReferenceKinds.RelationshipTuple,
                        tuple,
                        Detail: "No relationship path grants this relation.")],
                    Narrative: denyNarrative));
        }
        catch (Exception ex)
        {
            // An authorization PDP must never throw through to the caller: a not-configured, unreachable,
            // or otherwise-failing OpenFGA engine DENIES rather than surfacing a raw 500 from
            // /api/authz/evaluate. The cause is logged; callers get only the stable message above.
            _logger.LogWarning(
                ex,
                "OpenFGA Check failed for user={User} relation={Relation} object={Object}; failing closed (deny).",
                check.User, check.Relation, check.Object);
            return AccessDecision.Deny(new Reason(RebacReasonCodes.EngineUnavailable, EngineUnavailableMessage))
                .WithExplanation(new DecisionExplanation(
                    Engine: "openfga",
                    DeterminingRule: DeterminingRules.EngineUnavailable,
                    PolicyReferences: [new PolicyReference(
                        PolicyReferenceKinds.ReasonCode,
                        RebacReasonCodes.EngineUnavailable)],
                    Narrative: EngineUnavailableMessage));
        }
    }
}
