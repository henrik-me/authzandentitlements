using System.Security.Claims;
using AuthzEntitlements.Edge.Gateway.Auth;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace AuthzEntitlements.Edge.Gateway.Tests;

// Coarse scope checks: HasScope splitting + the requirement handler outcome.
public sealed class GatewayScopeRequirementTests
{
    private static ClaimsPrincipal Principal(params string[] scopeClaims)
    {
        var claims = scopeClaims.Select(s => new Claim(GatewayClaims.ScopeClaimType, s));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static async Task<bool> HandlerSucceedsAsync(ClaimsPrincipal user, string requiredScope)
    {
        var requirement = new GatewayScopeRequirement(requiredScope);
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await new GatewayScopeAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public void HasScope_True_WhenScopePresentInSpaceDelimitedClaim()
    {
        var user = Principal("openid bank.read bank.transactions.write");
        Assert.True(GatewayClaims.HasScope(user, "bank.read"));
        Assert.True(GatewayClaims.HasScope(user, "bank.transactions.write"));
    }

    [Fact]
    public void HasScope_False_WhenScopeAbsent()
    {
        var user = Principal("openid profile");
        Assert.False(GatewayClaims.HasScope(user, "bank.read"));
    }

    [Fact]
    public void HasScope_False_WhenNoScopeClaim()
    {
        Assert.False(GatewayClaims.HasScope(new ClaimsPrincipal(new ClaimsIdentity()), "bank.read"));
    }

    [Fact]
    public void HasScope_True_AcrossMultipleScopeClaims()
    {
        var user = Principal("openid", "bank.approvals.write");
        Assert.True(GatewayClaims.HasScope(user, "bank.approvals.write"));
        Assert.False(GatewayClaims.HasScope(user, "bank.read"));
    }

    [Fact]
    public async Task Handler_Succeeds_WhenScopePresent()
    {
        var user = Principal("bank.read");
        Assert.True(await HandlerSucceedsAsync(user, "bank.read"));
    }

    [Fact]
    public async Task Handler_DoesNotSucceed_WhenScopeMissing()
    {
        var user = Principal("openid");
        Assert.False(await HandlerSucceedsAsync(user, "bank.read"));
    }
}
