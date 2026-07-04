using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthzEntitlements.Benchmarks;

// Serializable shape of one benchmark run and its per-engine results. These records are the
// on-disk contract (persisted by ResultStore, compared by RegressionDetector), so field names
// and the schema version are stable: bump SchemaVersion on any breaking shape change.

public static class BenchmarkStatus
{
    // The engine ran and its latency distribution is populated.
    public const string Measured = "measured";

    // The engine was requested but not benchmarked (e.g. a live out-of-process engine that is
    // offline/unreachable); SkipReason explains why. A skipped engine never fails the run.
    public const string Skipped = "skipped";
}

// A latency distribution over a sample of per-evaluation durations, in milliseconds, plus the
// derived throughput. Produced by LatencyStatistics.Compute; an empty sample yields all-zero.
public sealed record LatencyStats(
    int Count,
    double MinMs,
    double MaxMs,
    double MeanMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double ThroughputPerSec)
{
    // The defined distribution for an empty sample: zero count, zero everywhere. Callers use this
    // for skipped engines so the JSON shape stays uniform (no nulls) and round-trips cleanly.
    public static LatencyStats Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}

// One engine's result within a run: either measured (Warm populated, ColdMs = first measured
// evaluation) or skipped (SkipReason set, Warm = LatencyStats.Empty).
public sealed record EngineBenchmark(
    string EngineName,
    string Status,
    string? SkipReason,
    int ScenarioCount,
    double ColdMs,
    LatencyStats Warm);

// A whole benchmark run: the metadata needed to trend results over time plus one EngineBenchmark
// per selected engine. TimestampUtc is ISO-8601 (round-trip "o") so runs sort lexically by time.
public sealed record BenchmarkRun(
    int SchemaVersion,
    string TimestampUtc,
    string GitSha,
    string MachineName,
    string RuntimeVersion,
    int Iterations,
    int Warmup,
    IReadOnlyList<EngineBenchmark> Engines)
{
    // The current on-disk schema version. Bump when the persisted shape changes incompatibly.
    public const int CurrentSchemaVersion = 1;
}

// Shared System.Text.Json configuration for every benchmark artefact: camelCase property names,
// indented output, and enum-as-string (defensive — the model uses string status constants today).
public static class BenchmarkJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    // Built once and frozen (MakeReadOnly) so this shared instance — which defines the on-disk
    // contract (Web camelCase, indented, enum-as-string) — cannot be mutated by any caller and
    // silently break persistence/round-tripping.
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
