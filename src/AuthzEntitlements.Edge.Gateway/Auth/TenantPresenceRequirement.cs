using Microsoft.AspNetCore.Authorization;

namespace AuthzEntitlements.Edge.Gateway.Auth;

// Coarse tenant-presence requirement. The edge only asserts that the token
// CARRIES a tenant claim; matching that tenant to a specific resource is a
// fine-grained decision that stays in Bank.Api and the future PDP.
public sealed class TenantPresenceRequirement : IAuthorizationRequirement
{
}

public sealed class TenantPresenceAuthorizationHandler
    : AuthorizationHandler<TenantPresenceRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantPresenceRequirement requirement)
    {
        if (context.User.GetTenant() is not null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
