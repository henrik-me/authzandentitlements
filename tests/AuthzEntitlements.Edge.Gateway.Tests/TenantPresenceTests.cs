using System.Security.Claims;
using AuthzEntitlements.Edge.Gateway.Auth;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace AuthzEntitlements.Edge.Gateway.Tests;

// Coarse tenant-presence check: a token must carry a non-blank tenant claim.
public sealed class TenantPresenceTests
{
    private static ClaimsPrincipal Principal(string? tenant)
    {
        var claims = tenant is null
            ? []
            : new[] { new Claim(GatewayClaims.TenantClaimType, tenant) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static async Task<bool> HandlerSucceedsAsync(ClaimsPrincipal user)
    {
        var requirement = new TenantPresenceRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await new TenantPresenceAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Succeeds_WhenTenantPresent()
    {
        Assert.True(await HandlerSucceedsAsync(Principal("CONTOSO")));
    }

    [Fact]
    public async Task DoesNotSucceed_WhenTenantAbsent()
    {
        Assert.False(await HandlerSucceedsAsync(Principal(null)));
    }

    [Fact]
    public async Task DoesNotSucceed_WhenTenantWhitespace()
    {
        Assert.False(await HandlerSucceedsAsync(Principal("   ")));
    }

    [Fact]
    public void GetTenant_ReturnsValue_OrNullWhenBlank()
    {
        Assert.Equal("FABRIKAM", Principal("FABRIKAM").GetTenant());
        Assert.Null(Principal(null).GetTenant());
        Assert.Null(Principal("  ").GetTenant());
    }

    [Fact]
    public void GetSubject_ReturnsValue_OrNullWhenBlank()
    {
        var withSub = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(GatewayClaims.SubjectClaimType, "user-123")], "TestAuth"));
        Assert.Equal("user-123", withSub.GetSubject());
        Assert.Null(new ClaimsPrincipal(new ClaimsIdentity()).GetSubject());
    }
}
