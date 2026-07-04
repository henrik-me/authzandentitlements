using Xunit;

namespace AuthzEntitlements.Compliance.Tests;

// Covers report assembly: the offline (no-client) path populates the deterministic sections and
// self-skips both live sections, and the live-client path collects them.
public sealed class ComplianceReportBuilderTests
{
    [Fact]
    public async Task NoClient_DeterministicSectionsPopulated_LiveSectionsSelfSkip()
    {
        var report = await ComplianceReportBuilder.BuildAsync(
            "2026-07-04T00:00:00.0000000+00:00", "sha1234", client: null,
            governanceUrl: null, principalId: "user-teller1");

        Assert.Equal(ComplianceReport.CurrentSchemaVersion, report.SchemaVersion);
        Assert.True(report.Sod.AllToxicCombinationsDenied);
        Assert.True(report.AuditIntegrity.AllTamperDetected);
        Assert.False(report.Certification.Collected);
        Assert.False(report.LeastPrivilege.Collected);
        Assert.Contains("no --governance-url", report.Certification.Reason);
    }

    [Fact]
    public async Task WithClient_LiveSectionsCollected()
    {
        var client = new FakeGovernanceClient
        {
            Campaigns = [new ReviewCampaignDto(Guid.NewGuid(), "c1", "CONTOSO", "Open", [])],
            AccessPackages = [new AccessPackageDto("p1", "P1", true, 60, ["BranchManager"])],
            Grants = [],
        };

        var report = await ComplianceReportBuilder.BuildAsync(
            "2026-07-04T00:00:00.0000000+00:00", "sha1234", client,
            governanceUrl: "http://localhost:5300", principalId: "user-teller1");

        Assert.True(report.Certification.Collected);
        Assert.True(report.LeastPrivilege.Collected);
        Assert.Single(report.Certification.Campaigns);
        Assert.Single(report.LeastPrivilege.AccessPackages);
    }

    [Fact]
    public void BuildDeterministic_ProducesAValidReport()
    {
        var report = ComplianceReportBuilder.BuildDeterministic(
            "2026-07-04T00:00:00.0000000+00:00", "sha1234", "user-teller1");

        Assert.All(report.Sod.Cases, c => Assert.True(c.Passed, c.Scenario));
        Assert.All(report.AuditIntegrity.Cases, c => Assert.True(c.Passed, c.Scenario));
    }
}
