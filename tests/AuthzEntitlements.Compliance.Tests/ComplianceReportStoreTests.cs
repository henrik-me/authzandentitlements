using Xunit;

namespace AuthzEntitlements.Compliance.Tests;

// Covers persistence round-tripping and the fail-closed contract on malformed/empty/missing/
// unsupported-schema input. Scratch files go under Path.GetTempPath(), never the repo root.
public sealed class ComplianceReportStoreTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "authz-compliance-tests-" + Guid.NewGuid().ToString("N"));

    private static ComplianceReport SampleReport() =>
        ComplianceReportBuilder.BuildDeterministic(
            "2026-07-04T12:34:56.7890000+00:00", "abc1234", "user-teller1");

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var report = SampleReport();

        var path = ComplianceReportStore.Save(report, _tempDir);
        var loaded = ComplianceReportStore.Load(path);

        Assert.Equal(report.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(report.GeneratedAtUtc, loaded.GeneratedAtUtc);
        Assert.Equal(report.GitSha, loaded.GitSha);
        Assert.Equal(report.Sod.CasesEvaluated, loaded.Sod.CasesEvaluated);
        Assert.True(loaded.Sod.AllToxicCombinationsDenied);
        Assert.True(loaded.AuditIntegrity.AllTamperDetected);
        Assert.False(loaded.Certification.Collected);
        Assert.False(loaded.LeastPrivilege.Collected);
    }

    [Fact]
    public void Save_WritesBothArtifacts()
    {
        var jsonPath = ComplianceReportStore.Save(SampleReport(), _tempDir);
        var markdownPath = Path.Combine(_tempDir, ComplianceReportStore.MarkdownFileName);

        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));
    }

    [Fact]
    public void Save_WritesCamelCaseJson()
    {
        var path = ComplianceReportStore.Save(SampleReport(), _tempDir);
        var json = File.ReadAllText(path);

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"generatedAtUtc\"", json);
        Assert.DoesNotContain("\"SchemaVersion\"", json);
    }

    [Fact]
    public void Load_MalformedJson_FailsClosed()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "{ this is not valid json ");

        var ex = Assert.Throws<ComplianceDataException>(() => ComplianceReportStore.Load(path));
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void Load_MissingFile_FailsClosed()
    {
        var path = Path.Combine(_tempDir, "does-not-exist.json");

        var ex = Assert.Throws<ComplianceDataException>(() => ComplianceReportStore.Load(path));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void Parse_EmptyContent_FailsClosed()
    {
        Assert.Throws<ComplianceDataException>(() => ComplianceReportStore.Parse("   ", "in-memory"));
    }

    [Fact]
    public void Parse_JsonNullLiteral_FailsClosed()
    {
        Assert.Throws<ComplianceDataException>(() => ComplianceReportStore.Parse("null", "in-memory"));
    }

    [Fact]
    public void Parse_UnsupportedSchemaVersion_FailsClosed()
    {
        var json = $$"""{"schemaVersion": {{ComplianceReport.CurrentSchemaVersion + 1}}}""";

        var ex = Assert.Throws<ComplianceDataException>(() => ComplianceReportStore.Parse(json, "in-memory"));
        Assert.Contains("schemaVersion", ex.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
