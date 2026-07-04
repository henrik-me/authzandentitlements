using AuthzEntitlements.Governance.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// Read-time expiry (IsActive) is how the service enforces time-bound access without a
// background sweeper, and the factory snapshots the package roles onto the issued grant
// (item b + grant factory).
public sealed class AccessGrantTests
{
    private static readonly DateTimeOffset Granted = GovernanceTestData.Now;
    private static readonly DateTimeOffset Expires = Granted.AddMinutes(480);

    [Fact]
    public void IsActive_BeforeExpiry_IsTrue()
    {
        var grant = GrantExpiringAt(Expires, revokedAt: null);

        Assert.True(grant.IsActive(Expires.AddMinutes(-1)));
    }

    [Fact]
    public void IsActive_AtExpiryInstant_IsFalse()
    {
        var grant = GrantExpiringAt(Expires, revokedAt: null);

        // The boundary is exclusive: now == ExpiresAt is already expired.
        Assert.False(grant.IsActive(Expires));
    }

    [Fact]
    public void IsActive_AfterExpiry_IsFalse()
    {
        var grant = GrantExpiringAt(Expires, revokedAt: null);

        Assert.False(grant.IsActive(Expires.AddMinutes(1)));
    }

    [Fact]
    public void IsActive_Revoked_IsFalseEvenBeforeExpiry()
    {
        var grant = GrantExpiringAt(Expires, revokedAt: Granted.AddMinutes(10));

        Assert.False(grant.IsActive(Granted.AddMinutes(20)));
    }

    [Fact]
    public void Factory_Create_SnapshotsPackageRolesSortedAndDeduped()
    {
        var request = GovernanceTestData.Request("user-compliance1", GovernanceTestData.Contoso, "quarter-end-close");
        var package = GovernanceTestData.Package(
            "quarter-end-close", 480, "ComplianceOfficer", "BranchManager", "BranchManager");

        var grant = AccessGrantFactory.Create(request, package, Granted);

        Assert.Equal(["BranchManager", "ComplianceOfficer"], grant.Roles.Select(r => r.RoleName));
        Assert.Equal(request.Id, grant.RequestId);
        Assert.Equal("user-compliance1", grant.PrincipalId);
        Assert.Equal(GovernanceTestData.Contoso, grant.TenantCode);
        Assert.Equal("quarter-end-close", grant.AccessPackageCode);
    }

    [Fact]
    public void Factory_Create_NoOverride_UsesPackageDefaultExpiry()
    {
        var request = GovernanceTestData.Request("user-compliance1", GovernanceTestData.Contoso, "quarter-end-close");
        var package = GovernanceTestData.Package("quarter-end-close", 480, "ComplianceOfficer");

        var grant = AccessGrantFactory.Create(request, package, Granted);

        Assert.Equal(Granted.AddMinutes(480), grant.ExpiresAt);
        Assert.True(grant.IsActive(Granted));
    }

    [Fact]
    public void Factory_Create_RequestedOverride_ShortensExpiry()
    {
        var request = GovernanceTestData.Request(
            "user-compliance1", GovernanceTestData.Contoso, "quarter-end-close", requestedMinutes: 60);
        var package = GovernanceTestData.Package("quarter-end-close", 480, "ComplianceOfficer");

        var grant = AccessGrantFactory.Create(request, package, Granted);

        Assert.Equal(Granted.AddMinutes(60), grant.ExpiresAt);
    }

    private static AccessGrant GrantExpiringAt(DateTimeOffset expiresAt, DateTimeOffset? revokedAt) =>
        GovernanceTestData.Grant(
            "user-compliance1", GovernanceTestData.Contoso, "quarter-end-close",
            Granted, expiresAt, ["ComplianceOfficer"], revokedAt);
}
