using AuthzEntitlements.Bank.Api.Domain;
using Microsoft.AspNetCore.Authorization;

namespace AuthzEntitlements.Bank.Api.Auth;

// Authorization policy contract for Bank.Api. Auth is an OUTER gate (defence in
// depth): the domain layer still enforces maker-checker, SoD, and tenant rules.
// Roles come from the "roles" claim (RoleClaimType); scopes from the space-
// delimited "scope" claim via ScopeAuthorizationHandler.
public static class AuthorizationSetup
{
    // Scope policy names (also the scope string each requires).
    public const string ScopeReadPolicy = "bank.read";
    public const string ScopeTransactionsWritePolicy = "bank.transactions.write";
    public const string ScopeApprovalsWritePolicy = "bank.approvals.write";

    // Composite write policies: a scope AND one of the eligible roles.
    public const string TransactionCreatePolicy = "bank.transaction.create";
    public const string ApprovalDecidePolicy = "bank.approval.decide";

    // Roles permitted to originate (make) a transaction.
    private static readonly string[] MakerEligibleRoles =
        [RoleNames.Teller, RoleNames.BranchManager, RoleNames.ComplianceOfficer];

    // Roles permitted to decide (check) an approval; mirrors the domain rule.
    private static readonly string[] CheckerEligibleRoles =
        [RoleNames.BranchManager, RoleNames.ComplianceOfficer];

    public static IServiceCollection AddBankAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

        services.AddAuthorizationBuilder()
            // Baseline role policies (roles come from the "roles" claim).
            .AddPolicy(RoleNames.Teller, p => p.RequireRole(RoleNames.Teller))
            .AddPolicy(RoleNames.BranchManager, p => p.RequireRole(RoleNames.BranchManager))
            .AddPolicy(RoleNames.ComplianceOfficer, p => p.RequireRole(RoleNames.ComplianceOfficer))
            .AddPolicy(RoleNames.Auditor, p => p.RequireRole(RoleNames.Auditor))
            // Coarse-grained scope policies.
            .AddPolicy(ScopeReadPolicy, p =>
                p.AddRequirements(new ScopeRequirement(ScopeReadPolicy)))
            .AddPolicy(ScopeTransactionsWritePolicy, p =>
                p.AddRequirements(new ScopeRequirement(ScopeTransactionsWritePolicy)))
            .AddPolicy(ScopeApprovalsWritePolicy, p =>
                p.AddRequirements(new ScopeRequirement(ScopeApprovalsWritePolicy)))
            // Composite write policies: scope AND an eligible role.
            .AddPolicy(TransactionCreatePolicy, p =>
                p.AddRequirements(new ScopeRequirement(ScopeTransactionsWritePolicy))
                    .RequireRole(MakerEligibleRoles))
            .AddPolicy(ApprovalDecidePolicy, p =>
                p.AddRequirements(new ScopeRequirement(ScopeApprovalsWritePolicy))
                    .RequireRole(CheckerEligibleRoles));

        return services;
    }
}
