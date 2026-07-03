using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace AuthzEntitlements.Edge.Gateway.Auth;

// Coarse scope requirement for a route class. Mirrors Bank.Api's ScopeRequirement
// but stays gateway-local: the edge only checks scope PRESENCE, never role or
// resource-tenant matching (those are fine-grained and belong downstream).
public sealed class GatewayScopeRequirement(string requiredScope) : IAuthorizationRequirement
{
    public string RequiredScope { get; } = requiredScope;
}

public sealed class GatewayScopeAuthorizationHandler
    : AuthorizationHandler<GatewayScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, GatewayScopeRequirement requirement)
    {
        if (GatewayClaims.HasScope(context.User, requirement.RequiredScope))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
