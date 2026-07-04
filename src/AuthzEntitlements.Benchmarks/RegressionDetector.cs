namespace AuthzEntitlements.Benchmarks;

// One engine's regression comparison: baseline vs current warm p95, the relative delta, and whether
// it counts as a regression under the detector's tolerance + absolute-floor policy.
public sealed record EngineRegression(
    string EngineName,
    double BaselineP95Ms,
    double CurrentP95Ms,
    double DeltaPct,
    bool Regressed);

// The result of comparing a current run against a baseline: one EngineRegression per engine present
// (and measured) in BOTH runs. HasRegression drives the `--check` exit code (the "alert").
public sealed record RegressionReport(IReadOnlyList<EngineRegression> Engines)
{
    public bool HasRegression => Engines.Any(e => e.Regressed);
}

// Compares a current BenchmarkRun against a baseline and flags per-engine warm-p95 regressions.
//
// An engine is flagged as regressed only when BOTH conditions hold:
//   1. Relative: current p95 exceeds baseline p95 by more than RelativeTolerance (default 25%).
//   2. Absolute: the increase exceeds AbsoluteFloorMs (default 0.10 ms).
// The absolute floor suppresses noise on sub-millisecond in-process engines, where a large relative
// swing can be a fraction of a microsecond. Engines present in only one run (e.g. a live engine that
// was skipped, or a newly added engine absent from the baseline) are NOT comparable and are omitted.
public static class RegressionDetector
{
    public const double DefaultRelativeTolerance = 0.25;
    public const double DefaultAbsoluteFloorMs = 0.10;

    public static RegressionReport Detect(
        BenchmarkRun baseline,
        BenchmarkRun current,
        double relativeTolerance = DefaultRelativeTolerance,
        double absoluteFloorMs = DefaultAbsoluteFloorMs)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var baselineByName = new Dictionary<string, EngineBenchmark>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in baseline.Engines.Where(e => e.Status == BenchmarkStatus.Measured))
        {
            // Fail closed on a malformed baseline (duplicate measured engine) rather than letting
            // ToDictionary throw a bare ArgumentException that escapes the harness uncaught.
            if (!baselineByName.TryAdd(e.EngineName, e))
            {
                throw new BenchmarkDataException(
                    $"Baseline contains duplicate measured engine '{e.EngineName}'.");
            }
        }

        var results = new List<EngineRegression>();
        foreach (var engine in current.Engines)
        {
            if (engine.Status != BenchmarkStatus.Measured)
            {
                continue;
            }

            if (!baselineByName.TryGetValue(engine.EngineName, out var baselineEngine))
            {
                // No comparable baseline for this engine — not a regression, just not comparable.
                continue;
            }

            var baselineP95 = baselineEngine.Warm.P95Ms;
            var currentP95 = engine.Warm.P95Ms;
            var increase = currentP95 - baselineP95;

            var exceedsRelative = currentP95 > baselineP95 * (1.0 + relativeTolerance);
            var exceedsAbsolute = increase > absoluteFloorMs;
            var regressed = exceedsRelative && exceedsAbsolute;

            results.Add(new EngineRegression(
                EngineName: engine.EngineName,
                BaselineP95Ms: baselineP95,
                CurrentP95Ms: currentP95,
                DeltaPct: DeltaPercent(baselineP95, currentP95),
                Regressed: regressed));
        }

        return new RegressionReport(results);
    }

    // Relative change as a percentage. Guards divide-by-zero: when the baseline is zero, a positive
    // current is reported as 100% (a full increase from nothing) and an equal zero as 0%.
    private static double DeltaPercent(double baseline, double current)
    {
        if (baseline > 0)
        {
            return (current - baseline) / baseline * 100.0;
        }

        return current > 0 ? 100.0 : 0.0;
    }
}
