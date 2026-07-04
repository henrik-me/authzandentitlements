using System.Diagnostics;
using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Benchmarks;

// Runs the identical shared scenario catalog through each selected engine and times every
// evaluation, so engines are compared apples-to-apples on the same questions.
//
// For an in-process engine the runner does a discarded warmup phase (JIT/allocation settling) then
// a measured phase, timing EACH evaluation allocation-free via Stopwatch.GetTimestamp() +
// Stopwatch.GetElapsedTime(). The FIRST measured evaluation is recorded separately as the COLD
// latency; the remaining measured evaluations form the WARM (steady-state) distribution. Both
// phases cycle through FintechScenarioCatalog.Scenarios round-robin, so with iterations >= scenario
// count every scenario is exercised.
//
// A live (out-of-process) engine is probed for reachability and self-skips when offline — it is
// never run by default and never fails the run.
public sealed class BenchmarkRunner
{
    private readonly int _warmup;
    private readonly int _iterations;
    private readonly int _liveProbeTimeoutMs;

    public BenchmarkRunner(int warmup, int iterations, int liveProbeTimeoutMs = 250)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(warmup);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
        _warmup = warmup;
        _iterations = iterations;
        _liveProbeTimeoutMs = liveProbeTimeoutMs;
    }

    // Runs every selected engine and assembles a BenchmarkRun carrying the supplied run metadata.
    public BenchmarkRun Run(
        IReadOnlyList<string> engineNames,
        string timestampUtc,
        string gitSha,
        string machineName,
        string runtimeVersion)
    {
        ArgumentNullException.ThrowIfNull(engineNames);

        var engines = new List<EngineBenchmark>(engineNames.Count);
        foreach (var name in engineNames)
        {
            engines.Add(RunEngine(name));
        }

        return new BenchmarkRun(
            SchemaVersion: BenchmarkRun.CurrentSchemaVersion,
            TimestampUtc: timestampUtc,
            GitSha: gitSha,
            MachineName: machineName,
            RuntimeVersion: runtimeVersion,
            Iterations: _iterations,
            Warmup: _warmup,
            Engines: engines);
    }

    // Runs a single engine: measure in-process engines; probe-and-skip live engines.
    public EngineBenchmark RunEngine(string name)
    {
        if (EngineCatalog.IsInProcess(name))
        {
            return Measure(name, EngineCatalog.CreateInProcessProvider(name));
        }

        if (EngineCatalog.IsLive(name))
        {
            return RunLive(name);
        }

        // Unknown engine names never reach here (BenchmarkOptions validates), but fail closed with a
        // skipped entry rather than throwing mid-run.
        return Skipped(name, $"Unknown engine '{name}'.");
    }

    // Times the shared catalog through one in-process provider and builds its EngineBenchmark.
    private EngineBenchmark Measure(string name, IAuthorizationDecisionProvider provider)
    {
        var scenarios = FintechScenarioCatalog.Scenarios;
        var count = scenarios.Count;

        // Warmup — discarded. Cycle the catalog so every scenario's code path is JITted/settled.
        for (var i = 0; i < _warmup; i++)
        {
            _ = provider.Evaluate(scenarios[i % count].Request);
        }

        // Measured — time each evaluation allocation-free.
        var samples = new double[_iterations];
        for (var i = 0; i < _iterations; i++)
        {
            var request = scenarios[i % count].Request;
            var start = Stopwatch.GetTimestamp();
            _ = provider.Evaluate(request);
            samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        // Cold = the first measured evaluation; warm = the steady-state remainder. With a single
        // measured evaluation the warm distribution is that one sample (defined behaviour).
        var coldMs = samples[0];
        var warmSamples = _iterations > 1 ? samples[1..] : samples;

        double warmSumMs = 0;
        foreach (var value in warmSamples)
        {
            warmSumMs += value;
        }

        var warm = LatencyStatistics.Compute(warmSamples, warmSumMs / 1000.0);

        return new EngineBenchmark(
            EngineName: name,
            Status: BenchmarkStatus.Measured,
            SkipReason: null,
            ScenarioCount: count,
            ColdMs: coldMs,
            Warm: warm);
    }

    // Probes a live engine; measured benchmarking of out-of-process engines is out of scope for this
    // harness, so a reachable engine is still skipped (with a distinct reason) and an unreachable one
    // self-skips as offline. Either way the run never fails on a live engine.
    private EngineBenchmark RunLive(string name)
    {
        var endpoint = EngineCatalog.LiveEndpointDescription(name);
        var reachable = EngineCatalog.ProbeLiveReachable(name, _liveProbeTimeoutMs);
        var reason = reachable
            ? $"Live engine '{name}' reachable at {endpoint} but out-of-process benchmarking is not " +
              "wired in this harness; skipped."
            : $"Live engine '{name}' unreachable at {endpoint} (offline); skipped.";
        return Skipped(name, reason);
    }

    private static EngineBenchmark Skipped(string name, string reason) =>
        new(
            EngineName: name,
            Status: BenchmarkStatus.Skipped,
            SkipReason: reason,
            ScenarioCount: 0,
            ColdMs: 0,
            Warm: LatencyStats.Empty);
}
