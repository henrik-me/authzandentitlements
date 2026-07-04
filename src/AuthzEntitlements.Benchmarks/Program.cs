using System.Globalization;
using System.Runtime.InteropServices;
using AuthzEntitlements.Benchmarks;

// Entry point for the PDP performance benchmark harness. Parses options (fail closed → exit 2),
// runs the selected engines over the shared scenario catalog, persists the run, prints a summary,
// and — under --check — compares against the baseline and exits non-zero on a regression (the
// "regression alert").

BenchmarkOptions options;
try
{
    options = BenchmarkOptions.Parse(args);
}
catch (OptionsParseException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}

if (options.Help)
{
    Console.WriteLine(BenchmarkOptions.UsageText());
    return 0;
}

var timestampUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
var runner = new BenchmarkRunner(options.Warmup, options.Iterations);

Console.WriteLine(
    $"Running {options.Iterations} iterations ({options.Warmup} warmup) over " +
    $"{string.Join(", ", options.Engines)}...");

var run = runner.Run(
    options.Engines,
    timestampUtc,
    ResultStore.CaptureGitSha(),
    Environment.MachineName,
    RuntimeInformation.FrameworkDescription);

PrintSummary(run);

var savedPath = ResultStore.Save(run, options.OutDir);
Console.WriteLine($"Results written to {savedPath}");

if (!options.Check)
{
    return 0;
}

BenchmarkRun baseline;
try
{
    baseline = ResultStore.Load(options.BaselinePath);
}
catch (BenchmarkDataException ex)
{
    Console.Error.WriteLine($"error: could not load baseline: {ex.Message}");
    return 1;
}

RegressionReport report;
try
{
    report = RegressionDetector.Detect(baseline, run);
}
catch (BenchmarkDataException ex)
{
    Console.Error.WriteLine($"error: could not evaluate regression: {ex.Message}");
    return 1;
}

PrintRegressionReport(report, options.BaselinePath);

if (report.HasRegression)
{
    Console.Error.WriteLine("error: performance regression detected (warm p95 exceeded baseline).");
    return 1;
}

return 0;

static void PrintSummary(BenchmarkRun run)
{
    Console.WriteLine();
    Console.WriteLine($"git={run.GitSha} machine={run.MachineName} runtime={run.RuntimeVersion}");
    Console.WriteLine(
        $"{"engine",-10} {"status",-9} {"cold(ms)",10} {"p50",8} {"p95",8} {"p99",8} {"ops/sec",12}");
    foreach (var engine in run.Engines)
    {
        if (engine.Status == BenchmarkStatus.Measured)
        {
            var w = engine.Warm;
            Console.WriteLine(
                $"{engine.EngineName,-10} {engine.Status,-9} {engine.ColdMs,10:F4} " +
                $"{w.P50Ms,8:F4} {w.P95Ms,8:F4} {w.P99Ms,8:F4} {w.ThroughputPerSec,12:F0}");
        }
        else
        {
            Console.WriteLine($"{engine.EngineName,-10} {engine.Status,-9} {engine.SkipReason}");
        }
    }

    Console.WriteLine();
}

static void PrintRegressionReport(RegressionReport report, string baselinePath)
{
    Console.WriteLine($"Regression check against baseline '{baselinePath}':");
    if (report.Engines.Count == 0)
    {
        Console.WriteLine("  (no comparable engines)");
        return;
    }

    foreach (var engine in report.Engines)
    {
        var verdict = engine.Regressed ? "REGRESSED" : "ok";
        Console.WriteLine(
            $"  {engine.EngineName,-10} baseline p95={engine.BaselineP95Ms:F4}ms " +
            $"current p95={engine.CurrentP95Ms:F4}ms delta={engine.DeltaPct:+0.0;-0.0}% [{verdict}]");
    }
}
