using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace AuthzEntitlements.Bank.Api.Auth;

// Authorization requirement for a single OAuth scope. Keycloak emits granted
// scopes space-delimited inside a single "scope" claim, so the handler must
// split that value rather than assume one claim per scope.
public sealed class ScopeRequirement(string requiredScope) : IAuthorizationRequirement
{
    public string RequiredScope { get; } = requiredScope;
}

public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    // The single claim Keycloak uses to carry the space-delimited scope list.
    public const string ScopeClaimType = "scope";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        if (HasScope(context.User, requirement.RequiredScope))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

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
}
