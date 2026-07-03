using System.Security.Claims;
using AuthzEntitlements.Bank.Api.Auth;
using AuthzEntitlements.Bank.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthzEntitlements.Bank.Api.Tests;

// Authorization-policy tests using synthetic ClaimsPrincipals — no live Keycloak
// or Postgres. They exercise the role/scope/tenant contract wired by
// AuthorizationSetup, the space-delimited scope split, and the tenant helper.
public sealed class AuthPolicyTests
{
    private const string RolesClaim = "roles";
    private const string ScopeClaim = "scope";
    private const string TenantClaim = "tenant";

    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBankAuthorization();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Principal(
        string[]? roles = null, string? scope = null, string? tenant = null)
    {
        var claims = new List<Claim>();
        foreach (var role in roles ?? [])
        {
            claims.Add(new Claim(RolesClaim, role));
        }

        if (scope is not null)
        {
            claims.Add(new Claim(ScopeClaim, scope));
        }

        if (tenant is not null)
        {
            claims.Add(new Claim(TenantClaim, tenant));
        }

        var identity = new ClaimsIdentity(
            claims, authenticationType: "TestAuth", nameType: "preferred_username", roleType: RolesClaim);
        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> AuthorizeAsync(ClaimsPrincipal user, string policy)
    {
        var authz = BuildAuthorizationService();
        var result = await authz.AuthorizeAsync(user, resource: null, policy);
        return result.Succeeded;
    }

    [Fact]
    public async Task RolePolicy_Allows_WhenRolePresent()
    {
        Assert.True(await AuthorizeAsync(Principal(roles: [RoleNames.Teller]), RoleNames.Teller));
    }

    [Fact]
    public async Task RolePolicy_Denies_WhenRoleAbsent()
    {
        Assert.False(await AuthorizeAsync(Principal(roles: [RoleNames.Auditor]), RoleNames.Teller));
    }

    [Fact]
    public async Task ReadScope_Allows_WhenScopePresentInSpaceDelimitedClaim()
    {
        var user = Principal(scope: "openid bank.read bank.transactions.write");
        Assert.True(await AuthorizeAsync(user, AuthorizationSetup.ScopeReadPolicy));
    }

    [Fact]
    public async Task ReadScope_Denies_WhenScopeAbsent()
    {
        var user = Principal(scope: "openid profile");
        Assert.False(await AuthorizeAsync(user, AuthorizationSetup.ScopeReadPolicy));
    }

    [Fact]
    public async Task ReadScope_Denies_WhenNoScopeClaim()
    {
        Assert.False(await AuthorizeAsync(Principal(), AuthorizationSetup.ScopeReadPolicy));
    }

    [Fact]
    public async Task TransactionCreate_Allows_WhenScopeAndMakerRolePresent()
    {
        var user = Principal(
            roles: [RoleNames.Teller], scope: "openid bank.transactions.write");
        Assert.True(await AuthorizeAsync(user, AuthorizationSetup.TransactionCreatePolicy));
    }

    [Fact]
    public async Task TransactionCreate_Denies_WhenScopeMissing()
    {
        var user = Principal(roles: [RoleNames.Teller], scope: "openid bank.read");
        Assert.False(await AuthorizeAsync(user, AuthorizationSetup.TransactionCreatePolicy));
    }

    [Fact]
    public async Task TransactionCreate_Denies_WhenRoleNotMaker()
    {
        // Auditor is not a maker-eligible role even with the write scope.
        var user = Principal(roles: [RoleNames.Auditor], scope: "bank.transactions.write");
        Assert.False(await AuthorizeAsync(user, AuthorizationSetup.TransactionCreatePolicy));
    }

    [Fact]
    public async Task ApprovalDecide_Allows_WhenBranchManagerWithScope()
    {
        var user = Principal(
            roles: [RoleNames.BranchManager], scope: "bank.approvals.write");
        Assert.True(await AuthorizeAsync(user, AuthorizationSetup.ApprovalDecidePolicy));
    }

    [Fact]
    public async Task ApprovalDecide_Denies_WhenTellerRole()
    {
        // Teller is not a checker-eligible role even with the approvals scope.
        var user = Principal(roles: [RoleNames.Teller], scope: "bank.approvals.write");
        Assert.False(await AuthorizeAsync(user, AuthorizationSetup.ApprovalDecidePolicy));
    }

    [Fact]
    public void ScopeHandler_HasScope_SplitsSpaceDelimitedValue()
    {
        var user = Principal(scope: "openid bank.read bank.approvals.write");
        Assert.True(ScopeAuthorizationHandler.HasScope(user, "bank.read"));
        Assert.True(ScopeAuthorizationHandler.HasScope(user, "bank.approvals.write"));
        Assert.False(ScopeAuthorizationHandler.HasScope(user, "bank.transactions.write"));
    }

    [Fact]
    public void TenantClaims_MatchesTenant_TrueOnExactMatch_FalseOtherwise()
    {
        var contoso = Principal(tenant: "CONTOSO");
        Assert.True(contoso.MatchesTenant("CONTOSO"));
        Assert.False(contoso.MatchesTenant("FABRIKAM"));
        Assert.Equal("CONTOSO", contoso.GetTenant());

        // A principal with no tenant claim fails closed.
        var noTenant = Principal();
        Assert.False(noTenant.MatchesTenant("CONTOSO"));
        Assert.Null(noTenant.GetTenant());
    }
}
