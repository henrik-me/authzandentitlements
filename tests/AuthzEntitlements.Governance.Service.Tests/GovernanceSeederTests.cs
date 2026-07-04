using AuthzEntitlements.Governance.Service.Data;
using AuthzEntitlements.Governance.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// The seeder must be deterministic so re-seeds and cross-service references stay stable
// (item a). The idempotency guard itself (AccessPackages.AnyAsync) is exercised at runtime
// against the database; here we pin the pure object graph it builds.
public sealed class GovernanceSeederTests
{
    [Fact]
    public void BuildAccessPackages_IsDeterministicAcrossCalls()
    {
        var first = GovernanceSeeder.BuildAccessPackages();
        var second = GovernanceSeeder.BuildAccessPackages();

        var firstIds = first.ToDictionary(p => p.Code, p => p.Id, StringComparer.Ordinal);
        foreach (var package in second)
        {
            Assert.Equal(firstIds[package.Code], package.Id);
        }
    }

    [Fact]
    public void BuildAccessPackages_HasTheThreeSeedPackages()
    {
        var codes = GovernanceSeeder.BuildAccessPackages()
            .Select(p => p.Code)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["branch-approver", "quarter-end-close", "treasury-oversight"], codes);
    }

    [Fact]
    public void BuildAccessPackages_QuarterEndClose_GrantsBranchManagerAndCompliance()
    {
        var quarterEndClose = Single(GovernanceCatalog.Packages.QuarterEndClose);

        Assert.Equal(480, quarterEndClose.DefaultDurationMinutes);
        Assert.True(quarterEndClose.RequiresApproval);
        Assert.Equal(
            ["BranchManager", "ComplianceOfficer"],
            quarterEndClose.Roles.Select(r => r.RoleName).OrderBy(r => r, StringComparer.Ordinal));
    }

    [Fact]
    public void BuildAccessPackages_BranchApprover_GrantsBranchManagerForSodDemo()
    {
        // branch-approver grants BranchManager; a Teller/Auditor requesting it is the
        // designed SoD-deny demonstration path.
        var branchApprover = Single(GovernanceCatalog.Packages.BranchApprover);

        var role = Assert.Single(branchApprover.Roles);
        Assert.Equal("BranchManager", role.RoleName);
    }

    [Fact]
    public void BuildPrincipals_MatchSeedUsersWithBaselineRoles()
    {
        var principals = GovernanceSeeder.BuildPrincipals();

        var byId = principals.ToDictionary(p => p.Id, StringComparer.Ordinal);
        Assert.Equal("Teller", Assert.Single(byId["user-teller1"].BaselineRoles).RoleName);
        Assert.Equal("BranchManager", Assert.Single(byId["user-manager1"].BaselineRoles).RoleName);
        Assert.Equal("ComplianceOfficer", Assert.Single(byId["user-compliance1"].BaselineRoles).RoleName);
        Assert.Equal("Auditor", Assert.Single(byId["user-auditor1"].BaselineRoles).RoleName);
    }

    [Fact]
    public void BuildPrincipals_AreAllContosoTenant()
    {
        var principals = GovernanceSeeder.BuildPrincipals();

        Assert.All(principals, p => Assert.Equal(GovernanceSeeder.ContosoCode, p.TenantCode));
    }

    private static AccessPackage Single(string code) =>
        GovernanceSeeder.BuildAccessPackages().Single(p => p.Code == code);
}
