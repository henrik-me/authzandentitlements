using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

namespace AuthzEntitlements.Authz.Pdp.Providers.SpiceDb;

// A resolved SpiceDB permission check: the bare subject id, the SpiceDB permission, and the bare
// account id the adapter asks SpiceDB about. Ids are bare ("carol", "acme-checking") because the
// SpiceDB gRPC API carries object type and id as separate fields; the fixed "user"/"account" types
// live in RebacTypes (shared with the OpenFGA adapter).
public readonly record struct SpiceDbCheck(string SubjectId, string Permission, string AccountId);

// The pure AuthZEN-request -> SpiceDB-permission-check mapping, factored out of the provider so it is
// unit testable with no client or server. It is the DIRECT COUNTERPART to OpenFgaRequestMapper and
// reuses the SHARED ReBAC vocabulary (RebacActionMap / RebacTypes / RebacReasonCodes), so SpiceDB and
// OpenFGA map the same AuthZEN request to the same relationship question — the whole point of the
// head-to-head. Fails closed identically: an action with no ReBAC relation -> UnknownAction; a
// non-account resource -> UnsupportedResourceType; a resource with no id -> MissingResourceId.
public static class SpiceDbRequestMapper
{
    // Returns true with a populated check when the request maps to a concrete permission check; false
    // with a populated denial otherwise. Exactly one of check/denial is meaningful per the return value.
    public static bool TryMap(AccessRequest request, out SpiceDbCheck check, out AccessDecision denial)
    {
        check = default;
        denial = default!;

        // Reuse the SHARED action->relation map: the OpenFGA "relation" name (can_view / can_transact)
        // is exactly the SpiceDB permission name, so SpiceDB and OpenFGA answer the identical question.
        if (!RebacActionMap.TryGetRelation(request.Action.Name, out var permission))
        {
            var message = $"Action '{request.Action.Name}' has no SpiceDB permission mapping.";
            denial = AccessDecision.Deny(new Reason(ReasonCodes.UnknownAction, message))
                .WithExplanation(BoundaryExplanation(ReasonCodes.UnknownAction, message));
            return false;
        }

        // Both mapped permissions (can_view / can_transact) are account permissions in the model, so
        // the adapter only answers account-shaped questions. A non-account resource (e.g. the CS05
        // "transaction" shape) is denied here rather than checking against an object type the schema
        // does not define — SpiceDB rejects such a check, so mapping it would surface an error instead
        // of a clean deny. Fail closed.
        if (!string.Equals(request.Resource.Type, RebacTypes.Account, StringComparison.Ordinal))
        {
            var message =
                $"The SpiceDB ReBAC adapter only evaluates '{RebacTypes.Account}' resources; " +
                $"'{request.Resource.Type}' has no queryable permission in the schema.";
            denial = AccessDecision.Deny(new Reason(RebacReasonCodes.UnsupportedResourceType, message))
                .WithExplanation(BoundaryExplanation(RebacReasonCodes.UnsupportedResourceType, message));
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Resource.Id))
        {
            var message =
                "A ReBAC check requires a concrete resource id (the bare id without a type prefix, " +
                "e.g. \"acme-checking\" — the adapter qualifies it as \"account:acme-checking\").";
            denial = AccessDecision.Deny(new Reason(RebacReasonCodes.MissingResourceId, message))
                .WithExplanation(BoundaryExplanation(RebacReasonCodes.MissingResourceId, message));
            return false;
        }

        check = new SpiceDbCheck(
            SubjectId: request.Subject.Id,
            Permission: permission,
            AccountId: request.Resource.Id);
        return true;
    }

    // The additive CS16 explanation for a fail-closed boundary denial, mirroring OpenFgaRequestMapper:
    // the determining rule is the shared normalization of the reason code, and the reason code itself is
    // surfaced as the native artifact. Additive only — the reason codes/messages and the TryMap bool
    // contract are unchanged.
    private static DecisionExplanation BoundaryExplanation(string reasonCode, string narrative) =>
        new(
            Engine: SpiceDbProvider.EngineName,
            DeterminingRule: DecisionExplanations.RuleForReason(reasonCode),
            PolicyReferences: [new PolicyReference(PolicyReferenceKinds.ReasonCode, reasonCode)],
            Narrative: narrative);
}
