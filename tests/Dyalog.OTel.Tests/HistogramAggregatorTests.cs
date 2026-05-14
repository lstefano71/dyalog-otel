using Dyalog.OTel.Pipeline;

namespace Dyalog.OTel.Tests;

public class HistogramAggregatorTests
{
    [Fact]
    public void Records_And_Snapshots_Correctly()
    {
        var agg = new HistogramAggregator();
        agg.Record("latency", 10.0);
        agg.Record("latency", 50.0);
        agg.Record("latency", 100.0);
        agg.Record("latency", 500.0);
        agg.Record("latency", 1000.0);

        var snapshots = agg.SnapshotAndReset();
        Assert.True(snapshots.ContainsKey("latency"));

        var snap = snapshots["latency"];
        Assert.Equal(5, snap.Count);
        Assert.Equal(1660.0, snap.Sum);
        Assert.Equal(10.0, snap.Min);
        Assert.Equal(1000.0, snap.Max);
        Assert.True(snap.BucketCounts.Length > 0);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var agg = new HistogramAggregator();
        agg.Record("latency", 42.0);
        agg.SnapshotAndReset();

        var snapshots = agg.SnapshotAndReset();
        Assert.True(snapshots.ContainsKey("latency"));
        Assert.Equal(0, snapshots["latency"].Count);
    }

    [Fact]
    public void MultipleMetrics_TrackedSeparately()
    {
        var agg = new HistogramAggregator();
        agg.Record("latency", 10.0);
        agg.Record("size", 1024.0);

        var snapshots = agg.SnapshotAndReset();
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(1, snapshots["latency"].Count);
        Assert.Equal(1, snapshots["size"].Count);
    }

    [Fact]
    public void Percentiles_AreReasonable()
    {
        var agg = new HistogramAggregator();
        // Record 100 values from 1 to 100
        for (int i = 1; i <= 100; i++)
            agg.Record("latency", i);

        var snapshots = agg.SnapshotAndReset();
        var snap = snapshots["latency"];

        Assert.Equal(100, snap.Count);
        Assert.Equal(5050.0, snap.Sum); // sum of 1..100
        Assert.Equal(1.0, snap.Min);
        Assert.Equal(100.0, snap.Max);

        // P50 should be around 50
        Assert.InRange(snap.P50, 45, 55);
        // P90 should be around 90
        Assert.InRange(snap.P90, 85, 95);
        // P99 should be around 99
        Assert.InRange(snap.P99, 95, 105);
    }

    [Fact]
    public void LargeValues_DoNotThrow()
    {
        var agg = new HistogramAggregator();
        // Values beyond HdrHistogram range should not throw
        agg.Record("big", 999_999_999_999.0);
        agg.Record("big", 1.0);

        var snapshots = agg.SnapshotAndReset();
        Assert.Equal(2, snapshots["big"].Count);
        Assert.Equal(1.0, snapshots["big"].Min);
    }
}
