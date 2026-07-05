using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

namespace AuthzEntitlements.Authz.Pdp.Providers.Keto;

// A resolved Keto permission check: the bare subject id, the Keto permission (relation), and the bare
// account id the adapter asks Keto about. Ids are bare ("carol", "acme-checking") because the Keto
// REST API carries object namespace and id as separate fields and the subject as a bare subject_id;
// the fixed "user"/"account" types live in RebacTypes (shared with the SpiceDB / OpenFGA adapters).
public readonly record struct KetoCheck(string SubjectId, string Permission, string AccountId);

// The pure AuthZEN-request -> Keto-permission-check mapping, factored out of the provider so it is
// unit testable with no client or server. It is the DIRECT COUNTERPART to SpiceDbRequestMapper /
// OpenFgaRequestMapper and reuses the SHARED ReBAC vocabulary (RebacActionMap / RebacTypes /
// RebacReasonCodes), so Keto, SpiceDB, and OpenFGA map the same AuthZEN request to the same
// relationship question — the whole point of the head-to-head. Fails closed identically: an action
// with no ReBAC relation -> UnknownAction; a non-account resource -> UnsupportedResourceType; a
// resource with no id -> MissingResourceId.
public static class KetoRequestMapper
{
    // Returns true with a populated check when the request maps to a concrete permission check; false
    // with a populated denial otherwise. Exactly one of check/denial is meaningful per the return value.
    public static bool TryMap(AccessRequest request, out KetoCheck check, out AccessDecision denial)
    {
        check = default;
        denial = default!;

        // Reuse the SHARED action->relation map: the OpenFGA "relation" name (can_view / can_transact)
        // is exactly the Keto permission name, so Keto, SpiceDB, and OpenFGA answer the identical
        // question.
        if (!RebacActionMap.TryGetRelation(request.Action.Name, out var permission))
        {
            var message = $"Action '{request.Action.Name}' has no Keto permission mapping.";
            denial = AccessDecision.Deny(new Reason(ReasonCodes.UnknownAction, message))
                .WithExplanation(BoundaryExplanation(ReasonCodes.UnknownAction, message));
            return false;
        }

        // Both mapped permissions (can_view / can_transact) are account permissions in the model, so
        // the adapter only answers account-shaped questions. A non-account resource (e.g. the CS05
        // "transaction" shape) is denied here rather than checking against a namespace the model does
        // not define — Keto would answer such a check with a plain deny, so mapping it would masquerade
        // as a real "no relationship" result. Fail closed.
        if (!string.Equals(request.Resource.Type, RebacTypes.Account, StringComparison.Ordinal))
        {
            var message =
                $"The Keto ReBAC adapter only evaluates '{RebacTypes.Account}' resources; " +
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

        check = new KetoCheck(
            SubjectId: request.Subject.Id,
            Permission: permission,
            AccountId: request.Resource.Id);
        return true;
    }

    // The additive CS16 explanation for a fail-closed boundary denial, mirroring SpiceDbRequestMapper:
    // the determining rule is the shared normalization of the reason code, and the reason code itself is
    // surfaced as the native artifact. Additive only — the reason codes/messages and the TryMap bool
    // contract are unchanged.
    private static DecisionExplanation BoundaryExplanation(string reasonCode, string narrative) =>
        new(
            Engine: KetoProvider.EngineName,
            DeterminingRule: DecisionExplanations.RuleForReason(reasonCode),
            PolicyReferences: [new PolicyReference(PolicyReferenceKinds.ReasonCode, reasonCode)],
            Narrative: narrative);
}
