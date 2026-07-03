using System.Security.Claims;

namespace AuthzEntitlements.Bank.Api.Auth;

// Reads and asserts the caller's tenant claim (a tenant Code such as CONTOSO or
// FABRIKAM). Used as token-level defence in depth: the domain layer already
// derives the tenant from the resource and never trusts the caller, but every
// write path additionally checks that the token's tenant matches the resource.
public static class TenantClaims
{
    // The custom Keycloak protocol-mapper claim carrying the tenant Code.
    public const string TenantClaimType = "tenant";

    // The subject claim. Keycloak sets each user's id to the Bank.Api User.Id, so
    // the token subject correlates directly to a domain user.
    public const string SubjectClaimType = "sub";

    // Returns the caller's subject id, or null when the claim is absent or not a GUID.
    public static Guid? GetSubjectId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(SubjectClaimType);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    // Returns the caller's tenant Code, or null when the claim is absent/blank.
    public static string? GetTenant(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(TenantClaimType);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // Returns true only when the caller carries a tenant claim that exactly
    // matches the resource tenant Code. A missing claim fails closed (false), so
    // callers can translate a false result into 403 semantics.
    public static bool MatchesTenant(this ClaimsPrincipal principal, string tenantCode)
    {
        var claim = principal.GetTenant();
        return claim is not null && string.Equals(claim, tenantCode, StringComparison.Ordinal);
    }
}
