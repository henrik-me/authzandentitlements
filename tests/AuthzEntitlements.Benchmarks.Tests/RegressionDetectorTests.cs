using AuthzEntitlements.Benchmarks;
using Xunit;

namespace AuthzEntitlements.Benchmarks.Tests;

// Covers the regression policy: relative tolerance, the absolute floor that suppresses sub-ms noise,
// clean within-tolerance runs, and the not-comparable cases (engine missing from baseline, skipped).
public sealed class RegressionDetectorTests
{
    private static BenchmarkRun RunWith(params (string Engine, double P95, string Status)[] engines)
    {
        var list = engines
            .Select(e => new EngineBenchmark(
                EngineName: e.Engine,
                Status: e.Status,
                SkipReason: e.Status == BenchmarkStatus.Skipped ? "offline" : null,
                ScenarioCount: e.Status == BenchmarkStatus.Measured ? 22 : 0,
                ColdMs: 0,
                Warm: e.Status == BenchmarkStatus.Measured
                    ? new LatencyStats(100, 0, 0, 0, 0, e.P95, 0, 0)
                    : LatencyStats.Empty))
            .ToArray();

        return new BenchmarkRun(
            BenchmarkRun.CurrentSchemaVersion, "2026-01-01T00:00:00.0000000+00:00",
            "sha", "machine", "runtime", 100, 10, list);
    }

    private static BenchmarkRun Measured(string engine, double p95) =>
        RunWith((engine, p95, BenchmarkStatus.Measured));

    [Fact]
    public void Detect_LargeIncreaseBeyondToleranceAndFloor_Regresses()
    {
        var baseline = Measured("reference", 1.0);
        var current = Measured("reference", 2.0);

        var report = RegressionDetector.Detect(baseline, current);

        Assert.True(report.HasRegression);
        var engine = Assert.Single(report.Engines);
        Assert.True(engine.Regressed);
        Assert.Equal(1.0, engine.BaselineP95Ms);
        Assert.Equal(2.0, engine.CurrentP95Ms);
        Assert.Equal(100.0, engine.DeltaPct, 6);
    }

    [Fact]
    public void Detect_WithinRelativeTolerance_DoesNotRegress()
    {
        // +10% is under the 25% default tolerance.
        var report = RegressionDetector.Detect(Measured("reference", 10.0), Measured("reference", 11.0));

        Assert.False(report.HasRegression);
        Assert.False(Assert.Single(report.Engines).Regressed);
    }

    [Fact]
    public void Detect_LargeRelativeButSubFloorAbsolute_IsSuppressed()
    {
        // 0.01ms -> 0.05ms is +400% relative but only +0.04ms absolute, under the 0.10ms floor.
        var report = RegressionDetector.Detect(Measured("cedar", 0.01), Measured("cedar", 0.05));

        Assert.False(report.HasRegression);
        var engine = Assert.Single(report.Engines);
        Assert.False(engine.Regressed);
    }

    [Fact]
    public void Detect_JustOverBothThresholds_Regresses()
    {
        // 1.0 -> 1.30: +30% (> 25%) and +0.30ms (> 0.10ms).
        var report = RegressionDetector.Detect(Measured("aspnet", 1.0), Measured("aspnet", 1.30));

        Assert.True(Assert.Single(report.Engines).Regressed);
    }

    [Fact]
    public void Detect_EngineMissingFromBaseline_IsNotComparable()
    {
        var baseline = Measured("reference", 1.0);
        var current = RunWith(
            ("reference", 1.0, BenchmarkStatus.Measured),
            ("casbin", 99.0, BenchmarkStatus.Measured));

        var report = RegressionDetector.Detect(baseline, current);

        Assert.DoesNotContain(report.Engines, e => e.EngineName == "casbin");
        Assert.Contains(report.Engines, e => e.EngineName == "reference");
    }

    [Fact]
    public void Detect_SkippedCurrentEngine_IsIgnored()
    {
        var baseline = Measured("opa", 1.0);
        var current = RunWith(("opa", 0.0, BenchmarkStatus.Skipped));

        var report = RegressionDetector.Detect(baseline, current);

        Assert.Empty(report.Engines);
        Assert.False(report.HasRegression);
    }

    [Fact]
    public void Detect_CustomTolerance_IsHonoured()
    {
        // +50% would pass the default 25% but not a 100% tolerance.
        var report = RegressionDetector.Detect(
            Measured("reference", 1.0), Measured("reference", 1.5), relativeTolerance: 1.0);

        Assert.False(report.HasRegression);
    }

    [Fact]
    public void Detect_ImprovedLatency_DoesNotRegress()
    {
        var report = RegressionDetector.Detect(Measured("reference", 5.0), Measured("reference", 2.0));

        var engine = Assert.Single(report.Engines);
        Assert.False(engine.Regressed);
        Assert.True(engine.DeltaPct < 0);
    }
}
