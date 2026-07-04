using AuthzEntitlements.Benchmarks;
using Xunit;

namespace AuthzEntitlements.Benchmarks.Tests;

// Covers the pure percentile/throughput maths: known fixtures, the nearest-rank definition, and the
// defined behaviour for empty/single-element samples (no throws).
public sealed class LatencyStatisticsTests
{
    [Fact]
    public void Compute_EmptySample_ReturnsEmptyStats()
    {
        var stats = LatencyStatistics.Compute([], 0);

        Assert.Equal(0, stats.Count);
        Assert.Equal(0, stats.MinMs);
        Assert.Equal(0, stats.MaxMs);
        Assert.Equal(0, stats.MeanMs);
        Assert.Equal(0, stats.P50Ms);
        Assert.Equal(0, stats.P95Ms);
        Assert.Equal(0, stats.P99Ms);
        Assert.Equal(0, stats.ThroughputPerSec);
    }

    [Fact]
    public void Compute_SingleElement_UsesThatValueEverywhere()
    {
        var stats = LatencyStatistics.Compute([4.2], 0.5);

        Assert.Equal(1, stats.Count);
        Assert.Equal(4.2, stats.MinMs);
        Assert.Equal(4.2, stats.MaxMs);
        Assert.Equal(4.2, stats.MeanMs);
        Assert.Equal(4.2, stats.P50Ms);
        Assert.Equal(4.2, stats.P95Ms);
        Assert.Equal(4.2, stats.P99Ms);
        Assert.Equal(2.0, stats.ThroughputPerSec, 6);
    }

    [Fact]
    public void Compute_MinMaxMean_AreCorrect()
    {
        var stats = LatencyStatistics.Compute([1, 2, 3, 4], 0.010);

        Assert.Equal(1, stats.MinMs);
        Assert.Equal(4, stats.MaxMs);
        Assert.Equal(2.5, stats.MeanMs);
    }

    [Fact]
    public void Compute_UnsortedInput_IsSortedInternally()
    {
        var stats = LatencyStatistics.Compute([5, 1, 3, 2, 4], 0.015);

        Assert.Equal(1, stats.MinMs);
        Assert.Equal(5, stats.MaxMs);
        Assert.Equal(3, stats.MeanMs);
    }

    [Fact]
    public void Compute_Percentiles_UseNearestRankOnHundredSampleFixture()
    {
        // 1..100 ascending. Nearest-rank: rank = ceil(p/100 * 100) = p, value at index p-1 = p.
        var samples = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();

        var stats = LatencyStatistics.Compute(samples, 1);

        Assert.Equal(50, stats.P50Ms);
        Assert.Equal(95, stats.P95Ms);
        Assert.Equal(99, stats.P99Ms);
    }

    [Fact]
    public void Percentile_NearestRank_RoundsUp()
    {
        // 10 samples 1..10. p95: rank = ceil(0.95 * 10) = 10 -> value 10. p50: ceil(5) = 5 -> 5.
        var sorted = Enumerable.Range(1, 10).Select(i => (double)i).ToArray();

        Assert.Equal(5, LatencyStatistics.Percentile(sorted, 50));
        Assert.Equal(10, LatencyStatistics.Percentile(sorted, 95));
        Assert.Equal(10, LatencyStatistics.Percentile(sorted, 99));
    }

    [Fact]
    public void Percentile_ZeroPercentile_ReturnsMinimum()
    {
        var sorted = new double[] { 2, 4, 6, 8 };

        Assert.Equal(2, LatencyStatistics.Percentile(sorted, 0));
    }

    [Fact]
    public void Percentile_OutOfRange_IsClamped()
    {
        var sorted = new double[] { 2, 4, 6, 8 };

        Assert.Equal(2, LatencyStatistics.Percentile(sorted, -5));
        Assert.Equal(8, LatencyStatistics.Percentile(sorted, 150));
    }

    [Fact]
    public void Percentile_EmptySample_ReturnsZero()
    {
        Assert.Equal(0, LatencyStatistics.Percentile([], 95));
    }

    [Fact]
    public void Compute_Throughput_IsCountOverElapsedSeconds()
    {
        var stats = LatencyStatistics.Compute([1, 1, 1, 1], 2.0);

        Assert.Equal(2.0, stats.ThroughputPerSec, 6);
    }

    [Fact]
    public void Compute_NonPositiveElapsed_ThroughputIsZero()
    {
        var stats = LatencyStatistics.Compute([1, 2, 3], 0);

        Assert.Equal(0, stats.ThroughputPerSec);
    }
}
