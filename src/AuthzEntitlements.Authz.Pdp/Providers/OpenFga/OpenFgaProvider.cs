using AuthzEntitlements.Authz.Pdp.Contracts;

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
public sealed class OpenFgaProvider : IAuthorizationDecisionProvider
{
    private readonly OpenFgaRebacService _service;

    public OpenFgaProvider(OpenFgaRebacService service)
    {
        _service = service;
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

        var allowed = _service
            .CheckAsync(check.User, check.Relation, check.Object)
            .GetAwaiter()
            .GetResult();

        return allowed
            ? AccessDecision.Permit(new Reason(
                ReasonCodes.Permit,
                $"OpenFGA grants '{check.Relation}' on '{check.Object}' to '{check.User}'."))
            : AccessDecision.Deny(new Reason(
                RebacReasonCodes.NoRelationship,
                $"OpenFGA finds no relationship granting '{check.Relation}' on '{check.Object}' to '{check.User}'."));
    }
}
