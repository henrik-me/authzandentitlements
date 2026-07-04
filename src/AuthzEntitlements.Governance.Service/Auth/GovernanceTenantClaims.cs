using System.Security.Claims;

namespace AuthzEntitlements.Governance.Service.Auth;

// CS29 — reads and compares the caller's tenant claim so the governance request endpoints can
// bind their tenant boundary to the validated token, never a caller-supplied field. Mirrors
// Bank.Api's TenantClaims contract (the same "tenant" Keycloak protocol-mapper claim carrying
// a tenant Code such as CONTOSO or FABRIKAM). Fails closed: an absent/blank claim resolves to
// null, which callers translate into 403 semantics.
public static class GovernanceTenantClaims
{
    // The custom Keycloak protocol-mapper claim carrying the tenant Code. Identical to
    // Bank.Api's TenantClaims.TenantClaimType so a single forwarded token scopes both services.
    public const string TenantClaimType = "tenant";

    // Returns the caller's tenant Code, or null when the claim is absent/blank.
    public static string? GetTenant(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(TenantClaimType);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // Fail-closed tenant equality: true only when BOTH the caller tenant and the resource
    // tenant are present and exactly (Ordinal) equal. A missing/blank value on either side
    // returns false, so a resource is never exposed to a caller whose tenant cannot be proven
    // to match — closing the cross-tenant confused-deputy gap. The request endpoints enforce
    // this equality at the DATABASE QUERY level (WHERE TenantCode == callerTenant, with a
    // non-blank callerTenant guaranteed by RequireTenant) so a cross-tenant row is never
    // materialised; this method is the canonical, unit-tested definition of that fail-closed
    // semantics (used by the tenant-claim tests and available for any future in-memory check).
    public static bool BelongsToTenant(string? callerTenant, string? resourceTenant) =>
        !string.IsNullOrWhiteSpace(callerTenant)
        && !string.IsNullOrWhiteSpace(resourceTenant)
        && string.Equals(callerTenant, resourceTenant, StringComparison.Ordinal);
}
