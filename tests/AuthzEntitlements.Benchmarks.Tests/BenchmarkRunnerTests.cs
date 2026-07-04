using AuthzEntitlements.Benchmarks;
using Xunit;

namespace AuthzEntitlements.Benchmarks.Tests;

// Covers the harness end-to-end over the in-process engines with tiny iteration counts: every
// in-process engine runs the full shared catalog and yields populated stats, results are
// deterministic in shape, and live engines self-skip (never run) when not requested.
public sealed class BenchmarkRunnerTests
{
    private static BenchmarkRun RunInProcess(IReadOnlyList<string> engines) =>
        new BenchmarkRunner(warmup: 5, iterations: 100).Run(
            engines, "2026-07-04T00:00:00.0000000+00:00", "sha", "machine", "runtime");

    [Fact]
    public void Run_InProcessEngines_AllMeasuredWithPopulatedStats()
    {
        var run = RunInProcess(EngineCatalog.InProcessEngineNames);

        Assert.Equal(EngineCatalog.InProcessEngineNames.Length, run.Engines.Count);
        foreach (var engine in run.Engines)
        {
            Assert.Equal(BenchmarkStatus.Measured, engine.Status);
            Assert.Null(engine.SkipReason);
            // The full shared fintech catalog ran for this engine.
            Assert.Equal(22, engine.ScenarioCount);
            Assert.True(engine.Warm.Count > 0, $"{engine.EngineName} warm distribution is empty");
            Assert.True(engine.Warm.MaxMs >= engine.Warm.MinMs);
            Assert.True(engine.Warm.P99Ms >= engine.Warm.P50Ms);
        }
    }

    [Fact]
    public void Run_EveryScenario_IsExercisedPerEngine()
    {
        // With iterations (100) >= scenario count (22), the round-robin exercises every scenario at
        // least once; the warm distribution therefore holds iterations-1 samples.
        var run = RunInProcess(["reference"]);
        var reference = Assert.Single(run.Engines);

        Assert.Equal(99, reference.Warm.Count);
    }

    [Fact]
    public void Run_DefaultInProcessSet_DoesNotIncludeLiveEngines()
    {
        var run = RunInProcess(EngineCatalog.InProcessEngineNames);

        Assert.DoesNotContain(run.Engines, e => EngineCatalog.IsLive(e.EngineName));
    }

    [Fact]
    public void Run_WhenLiveEnginesRequested_TheySelfSkipOffline()
    {
        // Live engines are only ever run on request; offline in the test/CI environment, they must
        // self-skip with a reason rather than fail the run.
        var run = new BenchmarkRunner(warmup: 2, iterations: 10, liveProbeTimeoutMs: 50).Run(
            EngineCatalog.AllEngineNames, "2026-07-04T00:00:00.0000000+00:00", "sha", "machine", "runtime");

        foreach (var name in EngineCatalog.LiveEngineNames)
        {
            var engine = Assert.Single(run.Engines, e => e.EngineName == name);
            Assert.Equal(BenchmarkStatus.Skipped, engine.Status);
            Assert.False(string.IsNullOrWhiteSpace(engine.SkipReason));
        }

        // In-process engines still measured alongside the skipped live ones.
        Assert.Contains(run.Engines, e => e.EngineName == "reference" && e.Status == BenchmarkStatus.Measured);
    }

    [Fact]
    public void RunEngine_UnknownName_SkipsRatherThanThrows()
    {
        var engine = new BenchmarkRunner(1, 1).RunEngine("nonexistent");

        Assert.Equal(BenchmarkStatus.Skipped, engine.Status);
    }

    [Fact]
    public void Run_ProducesRunMetadataAndSchemaVersion()
    {
        var run = RunInProcess(["reference"]);

        Assert.Equal(BenchmarkRun.CurrentSchemaVersion, run.SchemaVersion);
        Assert.Equal(100, run.Iterations);
        Assert.Equal(5, run.Warmup);
        Assert.Equal("machine", run.MachineName);
    }
}
