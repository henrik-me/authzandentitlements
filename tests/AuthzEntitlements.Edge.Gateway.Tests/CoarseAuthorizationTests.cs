using AuthzEntitlements.Edge.Gateway.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthzEntitlements.Edge.Gateway.Tests;

// Asserts the four coarse policies carry exactly the requirements the edge
// contract promises: authenticated + tenant presence for all, plus the correct
// scope for the read/write route classes and NO scope for the authenticated-only
// policy. This is the wiring the YARP routes depend on by policy name.
public sealed class CoarseAuthorizationTests
{
    private static async Task<AuthorizationPolicy> GetPolicyAsync(string name)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoarseAuthorization();
        var provider = services.BuildServiceProvider()
            .GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await provider.GetPolicyAsync(name);
        Assert.NotNull(policy);
        return policy!;
    }

    private static string? ScopeOf(AuthorizationPolicy policy)
        => policy.Requirements.OfType<GatewayScopeRequirement>()
            .SingleOrDefault()?.RequiredScope;

    [Fact]
    public async Task ReadPolicy_HasReadScope_Tenant_AndDenyAnonymous()
    {
        var policy = await GetPolicyAsync(CoarseAuthorization.ReadPolicy);
        Assert.Equal(CoarseAuthorization.ReadScope, ScopeOf(policy));
        Assert.Single(policy.Requirements.OfType<TenantPresenceRequirement>());
        Assert.Single(policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>());
    }

    [Fact]
    public async Task TransactionsWritePolicy_HasTransactionsWriteScope_AndTenant()
    {
        var policy = await GetPolicyAsync(CoarseAuthorization.TransactionsWritePolicy);
        Assert.Equal(CoarseAuthorization.TransactionsWriteScope, ScopeOf(policy));
        Assert.Single(policy.Requirements.OfType<TenantPresenceRequirement>());
        Assert.Single(policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>());
    }

    [Fact]
    public async Task ApprovalsWritePolicy_HasApprovalsWriteScope_AndTenant()
    {
        var policy = await GetPolicyAsync(CoarseAuthorization.ApprovalsWritePolicy);
        Assert.Equal(CoarseAuthorization.ApprovalsWriteScope, ScopeOf(policy));
        Assert.Single(policy.Requirements.OfType<TenantPresenceRequirement>());
        Assert.Single(policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>());
    }

    [Fact]
    public async Task AuthenticatedPolicy_HasTenant_ButNoScope()
    {
        var policy = await GetPolicyAsync(CoarseAuthorization.AuthenticatedPolicy);
        Assert.Null(ScopeOf(policy));
        Assert.Single(policy.Requirements.OfType<TenantPresenceRequirement>());
        Assert.Single(policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>());
    }
}
