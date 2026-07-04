using Xunit;

namespace AuthzEntitlements.Compliance.Tests;

// Covers the Markdown rendering: all four section headers appear, and an offline live section
// renders the exact reproduction command.
public sealed class MarkdownRendererTests
{
    [Fact]
    public void Render_IncludesAllFourSectionHeaders()
    {
        var report = ComplianceReportBuilder.BuildDeterministic(
            "2026-07-04T00:00:00.0000000+00:00", "sha1234", "user-teller1");

        var md = MarkdownRenderer.Render(report);

        Assert.Contains("# Compliance evidence pack", md);
        Assert.Contains("## 1. Segregation-of-duties evidence", md);
        Assert.Contains("## 2. Audit-integrity evidence", md);
        Assert.Contains("## 3. Access-certification evidence", md);
        Assert.Contains("## 4. Least-privilege attestation", md);
    }

    [Fact]
    public void Render_OfflineLiveSection_ShowsReproductionCommand()
    {
        var report = ComplianceReportBuilder.BuildDeterministic(
            "2026-07-04T00:00:00.0000000+00:00", "sha1234", "user-teller1");

        var md = MarkdownRenderer.Render(report);

        Assert.Contains("Collected: no", md);
        Assert.Contains("dotnet run --project src/AuthzEntitlements.Compliance", md);
    }

    [Fact]
    public async Task Render_CollectedCertification_ShowsCampaignRow()
    {
        var client = new FakeGovernanceClient
        {
            Campaigns =
            [
                new ReviewCampaignDto(Guid.NewGuid(), "Q3 recert", "CONTOSO", "Open",
                    [new ReviewItemDto("Certify")]),
            ],
        };
        var report = await ComplianceReportBuilder.BuildAsync(
            "2026-07-04T00:00:00.0000000+00:00", "sha1234", client,
            "http://localhost:5300", "user-teller1");

        var md = MarkdownRenderer.Render(report);

        Assert.Contains("Q3 recert", md);
    }
}
