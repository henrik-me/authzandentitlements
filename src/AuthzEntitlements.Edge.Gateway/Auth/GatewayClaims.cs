using System.Security.Claims;

namespace AuthzEntitlements.Edge.Gateway.Auth;

// Coarse claim contract for the edge gateway. The gateway is deliberately
// independent of Bank.Api and re-declares the Keycloak claim names it needs so
// it can enforce coarse checks (scope + tenant presence) without a code
// dependency on the domain API. Values MUST match the CS03 token contract.
public static class GatewayClaims
{
    // Keycloak carries the granted scopes space-delimited inside one "scope" claim.
    public const string ScopeClaimType = "scope";

    // Custom Keycloak protocol-mapper claims carried in the access token.
    public const string TenantClaimType = "tenant";
    public const string RolesClaimType = "roles";
    public const string SubjectClaimType = "sub";
    public const string NameClaimType = "preferred_username";

    // Returns true when any "scope" claim's space-delimited value contains the
    // required scope. Tolerates multiple scope claims (defensive) and trims empties.
    public static bool HasScope(ClaimsPrincipal principal, string requiredScope)
    {
        foreach (var claim in principal.FindAll(ScopeClaimType))
        {
            var scopes = claim.Value.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (scopes.Contains(requiredScope, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Returns the caller's tenant Code, or null when the claim is absent/blank.
    // A null result is the coarse fail-closed signal for tenant presence.
    public static string? GetTenant(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(TenantClaimType);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // Returns the caller's subject id, or null when the claim is absent/blank.
    // The gateway records the subject for audit only; it does not interpret it.
    public static string? GetSubject(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(SubjectClaimType);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
