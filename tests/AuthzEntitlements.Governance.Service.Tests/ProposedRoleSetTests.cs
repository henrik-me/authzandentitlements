using AuthzEntitlements.Governance.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// The proposed role set is the exact input to the SoD wire payload, so its dedup and
// ordering must be deterministic (item c).
public sealed class ProposedRoleSetTests
{
    [Fact]
    public void Compute_UnionsBaselineAndPackageRoles()
    {
        var result = ProposedRoleSet.Compute(
            baselineRoles: ["ComplianceOfficer"],
            packageRoles: ["BranchManager"]);

        Assert.Equal(["BranchManager", "ComplianceOfficer"], result);
    }

    [Fact]
    public void Compute_DeduplicatesOverlappingRoles()
    {
        var result = ProposedRoleSet.Compute(
            baselineRoles: ["ComplianceOfficer", "Teller"],
            packageRoles: ["ComplianceOfficer", "BranchManager"]);

        // ComplianceOfficer appears in both inputs but only once in the output.
        Assert.Equal(["BranchManager", "ComplianceOfficer", "Teller"], result);
    }

    [Fact]
    public void Compute_OrdersOrdinally()
    {
        var result = ProposedRoleSet.Compute(
            baselineRoles: ["Teller", "Auditor"],
            packageRoles: ["ComplianceOfficer"]);

        Assert.Equal(["Auditor", "ComplianceOfficer", "Teller"], result);
    }

    [Fact]
    public void Compute_EmptyInputs_ReturnsEmpty()
    {
        var result = ProposedRoleSet.Compute(baselineRoles: [], packageRoles: []);

        Assert.Empty(result);
    }
}
