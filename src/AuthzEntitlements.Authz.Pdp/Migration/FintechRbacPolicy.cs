using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Migration;

// The concrete fintech RBAC policy. Its role -> permission grant matrix mirrors
// ReferenceDecisionProvider's role eligibility EXACTLY on the role dimension, so the
// RBAC->ReBAC translation demonstrates a faithful migration of the real bank rules:
//   * Teller             -> may create a transaction (maker-eligible).
//   * BranchManager      -> may create/approve/reject a transaction and create an account.
//   * ComplianceOfficer  -> may create/approve/reject a transaction (maker + checker-eligible).
//   * Auditor            -> read-only; holds no role-gated write permission.
//
// Only the role-GATED bank actions appear as permissions. bank.account.read is scope-gated,
// not role-gated, so it is intentionally outside the RBAC role->permission matrix. The
// contextual/ABAC gates the reference engine also applies — scope, tenant, subject==maker,
// pending status, the maker-checker threshold, and segregation of duties — are not part of
// the pure-RBAC dimension; they belong to the ABAC engines (or ReBAC relationship/contextual
// tuples), not this role->permission translation.
public static class FintechRbacPolicy
{
    private const string TellerAnna = "teller-anna";
    private const string BranchMgrBen = "branch-mgr-ben";
    private const string ComplianceCara = "compliance-cara";
    private const string AuditorDan = "auditor-dan";
    private const string ManagerAndCompliance = "manager-and-compliance";

    public static RbacPolicy Policy { get; } = Build();

    private static RbacPolicy Build()
    {
        var roles = new[]
        {
            RoleNames.Teller,
            RoleNames.BranchManager,
            RoleNames.ComplianceOfficer,
            RoleNames.Auditor,
        };

        var permissions = new[]
        {
            ActionNames.AccountCreate,
            ActionNames.TransactionCreate,
            ActionNames.TransactionApprove,
            ActionNames.TransactionReject,
        };

        var rolePermissions = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [RoleNames.Teller] = new[] { ActionNames.TransactionCreate },
            [RoleNames.BranchManager] = new[]
            {
                ActionNames.AccountCreate,
                ActionNames.TransactionCreate,
                ActionNames.TransactionApprove,
                ActionNames.TransactionReject,
            },
            [RoleNames.ComplianceOfficer] = new[]
            {
                ActionNames.TransactionCreate,
                ActionNames.TransactionApprove,
                ActionNames.TransactionReject,
            },
            [RoleNames.Auditor] = Array.Empty<string>(),
        };

        var userRoles = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [TellerAnna] = new[] { RoleNames.Teller },
            [BranchMgrBen] = new[] { RoleNames.BranchManager },
            [ComplianceCara] = new[] { RoleNames.ComplianceOfficer },
            [AuditorDan] = new[] { RoleNames.Auditor },
            [ManagerAndCompliance] = new[] { RoleNames.BranchManager, RoleNames.ComplianceOfficer },
        };

        return RbacPolicy.Create(roles, permissions, rolePermissions, userRoles);
    }
}
