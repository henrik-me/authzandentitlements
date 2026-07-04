namespace AuthzEntitlements.Benchmarks;

// Pure, dependency-free latency statistics over a sample of per-evaluation durations (in
// milliseconds). Every method is deterministic and side-effect-free so it is fully unit-testable
// without a real engine.
//
// Percentile method: NEAREST-RANK on the ascending-sorted sample. For a percentile p in [0, 100]
// over N samples, the ordinal rank is ceil((p / 100) * N), and the value is the element at
// (rank - 1) clamped into [0, N - 1]. Nearest-rank is chosen (over linear interpolation) because it
// always returns an actually-observed sample value and needs no interpolation policy — a good fit
// for sub-millisecond in-process latencies where interpolation adds noise, not signal.
//
// Fail-closed / defined behaviour: an EMPTY sample yields LatencyStats.Empty (all zeros, never a
// throw); a SINGLE-element sample yields that value for min/max/mean and every percentile.
public static class LatencyStatistics
{
    // Computes the full latency distribution from a sample of durations (ms) and the total elapsed
    // wall time (seconds) the sample represents. Throughput is count / totalElapsedSeconds
    // (ops/sec); when the elapsed time is non-positive it is reported as zero rather than throwing.
    public static LatencyStats Compute(IReadOnlyList<double> samplesMs, double totalElapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(samplesMs);

        if (samplesMs.Count == 0)
        {
            return LatencyStats.Empty;
        }

        var sorted = samplesMs.ToArray();
        Array.Sort(sorted);

        var count = sorted.Length;
        var min = sorted[0];
        var max = sorted[count - 1];

        double sum = 0;
        foreach (var value in sorted)
        {
            sum += value;
        }

        var mean = sum / count;
        var throughput = totalElapsedSeconds > 0 ? count / totalElapsedSeconds : 0;

        return new LatencyStats(
            Count: count,
            MinMs: min,
            MaxMs: max,
            MeanMs: mean,
            P50Ms: Percentile(sorted, 50),
            P95Ms: Percentile(sorted, 95),
            P99Ms: Percentile(sorted, 99),
            ThroughputPerSec: throughput);
    }

    // Nearest-rank percentile over an already-ascending-sorted, non-empty array. See the class
    // comment for the exact rank formula. Percentiles outside [0, 100] are clamped to the bounds.
    public static double Percentile(IReadOnlyList<double> sortedAscending, double percentile)
    {
        ArgumentNullException.ThrowIfNull(sortedAscending);

        if (sortedAscending.Count == 0)
        {
            return 0;
        }

        var p = Math.Clamp(percentile, 0, 100);
        if (p <= 0)
        {
            return sortedAscending[0];
        }

        var rank = (int)Math.Ceiling(p / 100.0 * sortedAscending.Count);
        var index = Math.Clamp(rank - 1, 0, sortedAscending.Count - 1);
        return sortedAscending[index];
    }
}
