using Dyalog.OTel.Channels;
using Dyalog.OTel.Destinations;

namespace Dyalog.OTel.Tests;

public class TextHistogramSummaryEngineTests
{
    [Fact]
    public void DrainReady_GroupsSummariesPerEmitter()
    {
        var engine = new TextHistogramSummaryEngine(
        [
            new TextSummaryMetricRule("request.latency", TimeSpan.FromHours(1), [50d, 90d, 95d, 99d], IncludeBars: false)
        ]);

        engine.Observe(MetricAt("request.latency", 12, 05, 10), "CAL");
        engine.Observe(MetricAt("request.latency", 12, 15, 20), "SRV");
        engine.Observe(MetricAt("request.latency", 12, 25, 30), "CAL");

        var ready = engine.DrainReady(new DateTimeOffset(2026, 1, 2, 13, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, ready.Count);
        Assert.Contains(ready, block => block.Emitter == "CAL" && block.Count == 2);
        Assert.Contains(ready, block => block.Emitter == "SRV" && block.Count == 1);
    }

    [Fact]
    public void DrainPartial_ReturnsOpenWindowAsPartial()
    {
        var engine = new TextHistogramSummaryEngine(
        [
            new TextSummaryMetricRule("request.latency", TimeSpan.FromHours(1), [50d, 99d], IncludeBars: true)
        ]);

        engine.Observe(MetricAt("request.latency", 12, 10, 10), "CAL");

        var partial = engine.DrainPartial(new DateTimeOffset(2026, 1, 2, 12, 45, 0, TimeSpan.Zero));

        var summary = Assert.Single(partial);
        Assert.True(summary.IsPartial);
        Assert.Equal("CAL", summary.Emitter);
        Assert.Equal(1, summary.Count);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 12, 45, 0, TimeSpan.Zero), summary.TimestampUtc);
    }

    private static MetricPoint MetricAt(string name, int hour, int minute, double value) => new()
    {
        Name = name,
        Value = value,
        Type = MetricType.Histogram,
        TimestampUnixNano = ToUnixNano(new DateTimeOffset(2026, 1, 2, hour, minute, 0, TimeSpan.Zero))
    };

    private static long ToUnixNano(DateTimeOffset timestampUtc) =>
        (timestampUtc.UtcDateTime.Ticks - DateTime.UnixEpoch.Ticks) * 100;
}
