using AuthzEntitlements.Authz.Pdp.Contracts;
using Cerbos.Sdk.Builder;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cerbos;

// Pure mapping of an AuthZEN AccessRequest onto a Cerbos CheckResources request — no gRPC, no server,
// fully unit-testable offline. Every request targets ONE resource kind ("bank") and carries the
// single requested action; the principal carries the subject id/roles plus the tenant and coarse
// scopes as attributes, and the resource carries its tenant/amount/makerId/status attributes. The
// Cerbos "bank" policy reads exactly these attributes (P.attr.* / R.attr.*) to reproduce the
// reference engine's ordered checks and threshold obligation.
//
// Attribute hygiene: only NON-BLANK attributes are attached, so the policy's `has(...)` guards
// reflect true presence (a null/blank tenant fails the same-tenant check, matching the reference
// engine's fail-closed tenant rule). Scopes are ALWAYS attached (possibly an empty list) so the
// policy's `"x" in P.attr.scopes` checks never reference an absent attribute.
public static class CerbosRequestMapper
{
    // The single Cerbos resource kind the "bank" resource policy owns; every action is evaluated
    // against it. Matches `resourcePolicy.resource` in infra/cerbos/policies/bank.yaml.
    public const string ResourceKind = "bank";

    // Cerbos requires a non-empty resource id; catalog resources are often id-less (only the type +
    // ABAC attributes matter to the fintech rules), so an id-less resource uses this stable
    // placeholder. The provider Finds the result entry by the SAME id (see ResourceIdFor).
    public const string ResourceIdPlaceholder = "resource";

    // Cerbos requires a principal to carry at least one role; a role-less subject uses this
    // placeholder so the request is well-formed. It matches no eligible-role set, so a role gate
    // still denies (RoleNotAuthorized) exactly as the reference engine does for an ineligible role.
    private const string RolePlaceholder = "_no_role";

    // Principal/resource attribute keys the "bank" policy reads.
    private const string TenantAttr = "tenant";
    private const string ScopesAttr = "scopes";
    private const string AmountAttr = "amount";
    private const string MakerIdAttr = "makerId";
    private const string StatusAttr = "status";

    // The resource id used for this request (and to Find the matching result entry): the resource's
    // own id when present, else the stable placeholder.
    public static string ResourceIdFor(AccessRequest request) =>
        request.Resource.Id is { Length: > 0 } id ? id : ResourceIdPlaceholder;

    // Builds the full CheckResources request (single principal, single resource entry, single action).
    public static CheckResourcesRequest Map(AccessRequest request) =>
        CheckResourcesRequest.NewInstance()
            .WithRequestId(Guid.NewGuid().ToString())
            .WithIncludeMeta(false)
            .WithPrincipal(BuildPrincipal(request))
            .WithResourceEntries(new[] { BuildResourceEntry(request) });

    private static Principal BuildPrincipal(AccessRequest request)
    {
        var roles = request.Subject.Roles is { Count: > 0 } r
            ? r.ToArray()
            : new[] { RolePlaceholder };

        var principal = Principal.NewInstance(request.Subject.Id, roles);

        if (!string.IsNullOrWhiteSpace(request.Subject.Tenant))
        {
            principal = principal.WithAttribute(TenantAttr, AttributeValue.StringValue(request.Subject.Tenant));
        }

        // Always attach scopes (possibly empty) so the policy's `"x" in P.attr.scopes` never
        // references an absent attribute.
        var scopeValues = request.Context.Scopes
            .Select(AttributeValue.StringValue)
            .ToArray();
        principal = principal.WithAttribute(ScopesAttr, AttributeValue.ListValue(scopeValues));

        return principal;
    }

    private static ResourceEntry BuildResourceEntry(AccessRequest request)
    {
        var entry = ResourceEntry
            .NewInstance(ResourceKind, ResourceIdFor(request))
            .WithActions(new[] { request.Action.Name });

        if (!string.IsNullOrWhiteSpace(request.Resource.Tenant))
        {
            entry = entry.WithAttribute(TenantAttr, AttributeValue.StringValue(request.Resource.Tenant));
        }

        if (request.Resource.MakerId is { Length: > 0 } makerId)
        {
            entry = entry.WithAttribute(MakerIdAttr, AttributeValue.StringValue(makerId));
        }

        if (!string.IsNullOrWhiteSpace(request.Resource.Status))
        {
            entry = entry.WithAttribute(StatusAttr, AttributeValue.StringValue(request.Resource.Status));
        }

        if (request.Resource.Amount is { } amount)
        {
            entry = entry.WithAttribute(AmountAttr, AttributeValue.DoubleValue((double)amount));
        }

        return entry;
    }
}
