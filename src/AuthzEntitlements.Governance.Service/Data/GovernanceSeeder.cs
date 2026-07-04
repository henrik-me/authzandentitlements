using AuthzEntitlements.Governance.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Governance.Service.Data;

// Deterministic, idempotent seed for the access-governance catalog. Guards on
// AccessPackages.AnyAsync so re-running against an existing database is a no-op. Principal
// ids/roles/tenant match the CS02/CS03 Bank seed exactly so governance decisions line up
// with the users the rest of the system knows.
public static class GovernanceSeeder
{
    public const string ContosoCode = "CONTOSO";

    // Fixed ids so re-seeds and cross-service references stay stable.
    private static readonly Guid QuarterEndCloseId = new("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid TreasuryOversightId = new("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid BranchApproverId = new("c0000000-0000-0000-0000-000000000003");

    public static async Task SeedAsync(GovernanceDbContext db, CancellationToken ct = default)
    {
        if (await db.AccessPackages.AnyAsync(ct))
        {
            return;
        }

        db.AccessPackages.AddRange(BuildAccessPackages());
        db.Principals.AddRange(BuildPrincipals());

        await db.SaveChangesAsync(ct);
    }

    // The access-package catalog as a pure object graph (no persistence), so the
    // package -> roles composition is unit-testable without a database and the seeder has
    // a single definition of every package.
    public static IReadOnlyList<AccessPackage> BuildAccessPackages()
    {
        // Quarter-end close: elevates to BranchManager + ComplianceOfficer for a long
        // window. A ComplianceOfficer requesting this ends up holding both roles.
        var quarterEndClose = new AccessPackage
        {
            Id = QuarterEndCloseId,
            Code = GovernanceCatalog.Packages.QuarterEndClose,
            DisplayName = "Quarter-end close",
            Description = "Temporary elevation to run the quarter-end financial close.",
            DefaultDurationMinutes = 480,
            RequiresApproval = true,
        };
        AddRole(quarterEndClose, "c1000000-0000-0000-0000-000000000001", GovernanceCatalog.Roles.BranchManager);
        AddRole(quarterEndClose, "c1000000-0000-0000-0000-000000000002", GovernanceCatalog.Roles.ComplianceOfficer);

        var treasuryOversight = new AccessPackage
        {
            Id = TreasuryOversightId,
            Code = GovernanceCatalog.Packages.TreasuryOversight,
            DisplayName = "Treasury oversight",
            Description = "Temporary ComplianceOfficer access for treasury oversight duties.",
            DefaultDurationMinutes = 240,
            RequiresApproval = true,
        };
        AddRole(treasuryOversight, "c1000000-0000-0000-0000-000000000003", GovernanceCatalog.Roles.ComplianceOfficer);

        // Branch approver: grants BranchManager. A Teller or Auditor requesting this
        // creates a segregation-of-duties conflict, so the PDP SoD check denies it — the
        // demonstration path for a governance deny.
        var branchApprover = new AccessPackage
        {
            Id = BranchApproverId,
            Code = GovernanceCatalog.Packages.BranchApprover,
            DisplayName = "Branch approver",
            Description = "Temporary BranchManager approval authority for a branch.",
            DefaultDurationMinutes = 120,
            RequiresApproval = true,
        };
        AddRole(branchApprover, "c1000000-0000-0000-0000-000000000004", GovernanceCatalog.Roles.BranchManager);

        return [quarterEndClose, treasuryOversight, branchApprover];
    }

    // The principals as a pure object graph. Ids/roles/tenant match the CS02/CS03 seed
    // users so the governance service reasons about the same identities.
    public static IReadOnlyList<Principal> BuildPrincipals()
    {
        var teller = BuildPrincipal(
            "user-teller1", "Teller One",
            "d0000000-0000-0000-0000-000000000001", GovernanceCatalog.Roles.Teller);
        var manager = BuildPrincipal(
            "user-manager1", "Manager One",
            "d0000000-0000-0000-0000-000000000002", GovernanceCatalog.Roles.BranchManager);
        var compliance = BuildPrincipal(
            "user-compliance1", "Compliance One",
            "d0000000-0000-0000-0000-000000000003", GovernanceCatalog.Roles.ComplianceOfficer);
        var auditor = BuildPrincipal(
            "user-auditor1", "Auditor One",
            "d0000000-0000-0000-0000-000000000004", GovernanceCatalog.Roles.Auditor);

        return [teller, manager, compliance, auditor];
    }

    private static Principal BuildPrincipal(string id, string displayName, string roleId, string roleName)
    {
        var principal = new Principal
        {
            Id = id,
            TenantCode = ContosoCode,
            DisplayName = displayName,
        };
        principal.BaselineRoles.Add(new PrincipalRole
        {
            Id = new Guid(roleId),
            PrincipalId = id,
            RoleName = roleName,
        });
        return principal;
    }

    private static void AddRole(AccessPackage package, string roleId, string roleName) =>
        package.Roles.Add(new AccessPackageRole
        {
            Id = new Guid(roleId),
            AccessPackageId = package.Id,
            RoleName = roleName,
        });
}
