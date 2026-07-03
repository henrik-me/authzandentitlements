using Microsoft.AspNetCore.Authorization;

namespace AuthzEntitlements.Edge.Gateway.Auth;

// Registers the four coarse-grained edge policies. Every policy requires an
// authenticated user (so anonymous requests challenge 401) and a tenant claim
// (403 when absent). Read/write policies add the required scope for their route
// class. NO role, resource-tenant, maker-checker, or ABAC checks live here —
// those are fine-grained and stay in Bank.Api and the future PDP.
public static class CoarseAuthorization
{
    // Coarse policy names (referenced by the YARP routes in appsettings.json).
    public const string ReadPolicy = "coarse.read";
    public const string TransactionsWritePolicy = "coarse.transactions.write";
    public const string ApprovalsWritePolicy = "coarse.approvals.write";
    public const string AuthenticatedPolicy = "coarse.authenticated";

    // Scopes required per route class (must match the CS03 token contract).
    public const string ReadScope = "bank.read";
    public const string TransactionsWriteScope = "bank.transactions.write";
    public const string ApprovalsWriteScope = "bank.approvals.write";

    public static IServiceCollection AddCoarseAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, GatewayScopeAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, TenantPresenceAuthorizationHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(ReadPolicy, p => p
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantPresenceRequirement())
                .AddRequirements(new GatewayScopeRequirement(ReadScope)))
            .AddPolicy(TransactionsWritePolicy, p => p
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantPresenceRequirement())
                .AddRequirements(new GatewayScopeRequirement(TransactionsWriteScope)))
            .AddPolicy(ApprovalsWritePolicy, p => p
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantPresenceRequirement())
                .AddRequirements(new GatewayScopeRequirement(ApprovalsWriteScope)))
            // Authenticated + tenant presence only. POST /api/accounts is role-gated
            // downstream at the API; the edge only asserts a valid, tenant-bearing token.
            .AddPolicy(AuthenticatedPolicy, p => p
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantPresenceRequirement()));

        return services;
    }
}
