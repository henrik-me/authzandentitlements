using Xunit;

namespace AuthzEntitlements.Compliance.Tests;

// Covers the live-probe reporters: the offline self-skip path, the collected/summary path, and the
// fail-closed propagation of a reached-but-malformed response.
public sealed class LiveProbeReporterTests
{
    private const string Repro = "aspire run; curl ...";

    [Fact]
    public async Task Certification_Unreachable_SelfSkips()
    {
        var client = new FakeGovernanceClient { Unreachable = true };

        var section = await CertificationReporter.CollectAsync(client, Repro, CancellationToken.None);

        Assert.False(section.Collected);
        Assert.Contains("offline", section.Reason);
        Assert.Empty(section.Campaigns);
    }

    [Fact]
    public async Task Certification_CountsItemsByDecision()
    {
        var client = new FakeGovernanceClient
        {
            Campaigns =
            [
                new ReviewCampaignDto(
                    Guid.NewGuid(), "Q3 recert", "CONTOSO", "Open",
                    [
                        new ReviewItemDto("Certify"),
                        new ReviewItemDto("Certify"),
                        new ReviewItemDto("Revoke"),
                        new ReviewItemDto("Pending"),
                    ]),
            ],
        };

        var section = await CertificationReporter.CollectAsync(client, Repro, CancellationToken.None);

        Assert.True(section.Collected);
        var campaign = Assert.Single(section.Campaigns);
        Assert.Equal(4, campaign.TotalItems);
        Assert.Equal(2, campaign.Certified);
        Assert.Equal(1, campaign.Revoked);
        Assert.Equal(1, campaign.Pending);
    }

    [Fact]
    public async Task Certification_Malformed_FailsClosed()
    {
        var client = new FakeGovernanceClient { Malformed = true };

        await Assert.ThrowsAsync<ComplianceDataException>(
            () => CertificationReporter.CollectAsync(client, Repro, CancellationToken.None));
    }

    [Fact]
    public async Task LeastPrivilege_Unreachable_SelfSkips()
    {
        var client = new FakeGovernanceClient { Unreachable = true };

        var section = await LeastPrivilegeReporter.CollectAsync(
            client, Repro, "user-teller1", CancellationToken.None);

        Assert.False(section.Collected);
        Assert.Null(section.ProbedPrincipalId);
        Assert.Empty(section.Grants);
    }

    [Fact]
    public async Task LeastPrivilege_AttestsPackagesAndGrants()
    {
        var now = DateTimeOffset.UtcNow;
        var client = new FakeGovernanceClient
        {
            AccessPackages =
            [
                new AccessPackageDto("branch-approver", "Branch approver", true, 120, ["BranchManager"]),
            ],
            Grants =
            [
                new AccessGrantDto(
                    Guid.NewGuid(), "user-teller1", "branch-approver", "active", true,
                    now.AddHours(-1), now.AddHours(1)),
                new AccessGrantDto(
                    Guid.NewGuid(), "user-teller1", "treasury-oversight", "expired", false,
                    now.AddDays(-2), now.AddDays(-1)),
            ],
        };

        var section = await LeastPrivilegeReporter.CollectAsync(
            client, Repro, "user-teller1", CancellationToken.None);

        Assert.True(section.Collected);
        Assert.Equal("user-teller1", section.ProbedPrincipalId);
        Assert.Single(section.AccessPackages);
        Assert.Equal(2, section.Grants.Count);
        Assert.Contains(section.Grants, g => g.Active && g.Status == "active");
        Assert.Contains(section.Grants, g => !g.Active && g.Status == "expired");
    }

    [Fact]
    public async Task LeastPrivilege_Malformed_FailsClosed()
    {
        var client = new FakeGovernanceClient { Malformed = true };

        await Assert.ThrowsAsync<ComplianceDataException>(
            () => LeastPrivilegeReporter.CollectAsync(client, Repro, "user-teller1", CancellationToken.None));
    }

    [Fact]
    public void ReproductionCommands_IncludeEndpointsAndPrincipal()
    {
        var cert = CertificationReporter.ReproductionCommand("http://localhost:5300");
        var lp = LeastPrivilegeReporter.ReproductionCommand("http://localhost:5300", "user-manager1");

        Assert.Contains("/api/governance/review-campaigns", cert);
        Assert.Contains("/api/governance/access-packages", lp);
        Assert.Contains("user-manager1", lp);
    }
}
