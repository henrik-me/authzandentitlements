using System.Security.Claims;
using AuthzEntitlements.Governance.Service.Auth;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// CS29 — the tenant-claim reader + fail-closed tenant equality that the access-request
// endpoints bind their tenant boundary to. Exercised with synthetic ClaimsPrincipals — no
// live Keycloak or Postgres — mirroring the pure-decision style used across this service
// (GovernanceRules etc.). These lock the exact decisions the endpoints make: the list filter
// (BelongsToTenant true only for the caller's tenant), the cross-tenant decide guard (false
// -> 404), and the missing-tenant guard (GetTenant null -> 403).
public sealed class GovernanceTenantClaimsTests
{
    private const string Contoso = "CONTOSO";
    private const string Fabrikam = "FABRIKAM";

    private static ClaimsPrincipal PrincipalWith(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void GetTenant_ReturnsClaimValue_WhenPresent()
    {
        var user = PrincipalWith((GovernanceTenantClaims.TenantClaimType, Contoso));
        Assert.Equal(Contoso, user.GetTenant());
    }

    [Fact]
    public void GetTenant_FailsClosed_WhenClaimAbsent()
    {
        // A token that authenticated but carries no tenant claim -> null -> the endpoint 403s.
        var user = PrincipalWith(("sub", "user-1"));
        Assert.Null(user.GetTenant());
    }

    [Fact]
    public void GetTenant_FailsClosed_WhenClaimBlank()
    {
        var user = PrincipalWith((GovernanceTenantClaims.TenantClaimType, "   "));
        Assert.Null(user.GetTenant());
    }

    [Fact]
    public void BelongsToTenant_True_OnExactMatch()
    {
        Assert.True(GovernanceTenantClaims.BelongsToTenant(Contoso, Contoso));
    }

    [Fact]
    public void BelongsToTenant_False_OnDifferentTenant()
    {
        // The confused-deputy case: a CONTOSO caller must not reach a FABRIKAM request.
        Assert.False(GovernanceTenantClaims.BelongsToTenant(Contoso, Fabrikam));
    }

    [Fact]
    public void BelongsToTenant_IsOrdinalCaseSensitive()
    {
        // Tenant Codes are compared Ordinal (same as the domain sets), so case differences
        // fail closed rather than silently matching.
        Assert.False(GovernanceTenantClaims.BelongsToTenant("Contoso", Contoso));
    }

    [Fact]
    public void BelongsToTenant_FailsClosed_OnNullOrBlank()
    {
        Assert.False(GovernanceTenantClaims.BelongsToTenant(null, Contoso));
        Assert.False(GovernanceTenantClaims.BelongsToTenant(Contoso, null));
        Assert.False(GovernanceTenantClaims.BelongsToTenant("", Contoso));
        Assert.False(GovernanceTenantClaims.BelongsToTenant(Contoso, "   "));
        Assert.False(GovernanceTenantClaims.BelongsToTenant(null, null));
    }

    [Fact]
    public void ListFilter_KeepsOnlyCallerTenant()
    {
        // Models the ListRequestsAsync predicate (r.TenantCode == callerTenant): given a mixed
        // set, only the caller's tenant rows are visible — a caller never sees another tenant's
        // requests.
        var requests = new (string Id, string Tenant)[]
        {
            ("r1", Contoso),
            ("r2", Fabrikam),
            ("r3", Contoso),
            ("r4", "TAILSPIN"),
        };

        var visible = requests
            .Where(r => GovernanceTenantClaims.BelongsToTenant(Contoso, r.Tenant))
            .Select(r => r.Id)
            .ToArray();

        Assert.Equal(new[] { "r1", "r3" }, visible);
    }
}
