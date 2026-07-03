using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

// A resolved OpenFGA Check: the fully-qualified user string, the relation, and the "type:id"
// object the adapter asks OpenFGA about.
public readonly record struct RebacCheck(string User, string Relation, string Object);

// The pure AuthZEN-request -> OpenFGA-Check mapping, factored out of the provider so it is unit
// testable with no client or server. Fails closed: an action with no ReBAC relation maps to an
// UnknownAction deny, and a resource with no id maps to a MissingResourceId deny — a ReBAC Check
// is never issued against an unknown relation or a blank object.
public static class OpenFgaRequestMapper
{
    // Returns true with a populated check when the request maps to a concrete Check; false with a
    // populated denial otherwise. Exactly one of check/denial is meaningful per the return value.
    public static bool TryMap(AccessRequest request, out RebacCheck check, out AccessDecision denial)
    {
        check = default;
        denial = default!;

        if (!RebacActionMap.TryGetRelation(request.Action.Name, out var relation))
        {
            denial = AccessDecision.Deny(new Reason(
                ReasonCodes.UnknownAction,
                $"Action '{request.Action.Name}' has no OpenFGA relation mapping."));
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Resource.Id))
        {
            denial = AccessDecision.Deny(new Reason(
                RebacReasonCodes.MissingResourceId,
                "A ReBAC check requires a concrete resource id (e.g. \"account:acme-checking\")."));
            return false;
        }

        check = new RebacCheck(
            User: $"{RebacTypes.User}:{request.Subject.Id}",
            Relation: relation,
            Object: $"{request.Resource.Type}:{request.Resource.Id}");
        return true;
    }
}
