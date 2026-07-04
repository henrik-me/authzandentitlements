using AuthzEntitlements.Benchmarks;
using Xunit;

namespace AuthzEntitlements.Benchmarks.Tests;

// Covers persistence round-tripping and the fail-closed contract on malformed/empty/missing input.
// Scratch files go under Path.GetTempPath(), never the repo root.
public sealed class ResultStoreTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "authz-bench-tests-" + Guid.NewGuid().ToString("N"));

    private static BenchmarkRun SampleRun() =>
        new(
            BenchmarkRun.CurrentSchemaVersion,
            "2026-07-04T12:34:56.7890000+00:00",
            "abc1234",
            "test-machine",
            ".NET 10.0",
            1000,
            100,
            [
                new EngineBenchmark(
                    "reference", BenchmarkStatus.Measured, null, 22, 0.0123,
                    new LatencyStats(999, 0.001, 0.5, 0.01, 0.008, 0.02, 0.05, 100000)),
                new EngineBenchmark(
                    "opa", BenchmarkStatus.Skipped, "offline", 0, 0, LatencyStats.Empty),
            ]);

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var run = SampleRun();

        var path = ResultStore.Save(run, _tempDir);
        var loaded = ResultStore.Load(path);

        Assert.Equal(run.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(run.TimestampUtc, loaded.TimestampUtc);
        Assert.Equal(run.GitSha, loaded.GitSha);
        Assert.Equal(run.Engines.Count, loaded.Engines.Count);
        Assert.Equal("reference", loaded.Engines[0].EngineName);
        Assert.Equal(BenchmarkStatus.Measured, loaded.Engines[0].Status);
        Assert.Equal(0.02, loaded.Engines[0].Warm.P95Ms, 6);
        Assert.Equal(BenchmarkStatus.Skipped, loaded.Engines[1].Status);
        Assert.Equal("offline", loaded.Engines[1].SkipReason);
    }

    [Fact]
    public void Save_WritesCamelCaseJson()
    {
        var path = ResultStore.Save(SampleRun(), _tempDir);
        var json = File.ReadAllText(path);

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"engineName\"", json);
        Assert.DoesNotContain("\"SchemaVersion\"", json);
    }

    [Fact]
    public void Load_MalformedJson_FailsClosed()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "{ this is not valid json ");

        var ex = Assert.Throws<BenchmarkDataException>(() => ResultStore.Load(path));
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void Load_MissingFile_FailsClosed()
    {
        var path = Path.Combine(_tempDir, "does-not-exist.json");

        var ex = Assert.Throws<BenchmarkDataException>(() => ResultStore.Load(path));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void Parse_EmptyContent_FailsClosed()
    {
        Assert.Throws<BenchmarkDataException>(() => ResultStore.Parse("   ", "in-memory"));
    }

    [Fact]
    public void Parse_JsonNullLiteral_FailsClosed()
    {
        Assert.Throws<BenchmarkDataException>(() => ResultStore.Parse("null", "in-memory"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
