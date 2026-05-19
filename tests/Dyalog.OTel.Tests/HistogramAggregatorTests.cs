using System.Collections.Frozen;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Pipeline;

using PipelineInstance = Dyalog.OTel.Pipeline.Pipeline;

namespace Dyalog.OTel.Tests;

public class MetricAggregatorTests
{
    [Fact]
    public void Counter_SumsDeltasPerSeries()
    {
        var agg = new MetricAggregator();
        agg.Record(MakePoint("requests.count", MetricType.Counter, 1));
        agg.Record(MakePoint("requests.count", MetricType.Counter, 1));
        agg.Record(MakePoint("requests.count", MetricType.Counter, 3));

        var result = agg.SnapshotAndReset();
        var point = Assert.Single(result, p => p.Name == "requests.count");
        Assert.Equal(5.0, point.Value);
        Assert.Equal(MetricType.Counter, point.Type);
        Assert.NotNull(point.StartTimeUnixNano);
    }

    [Fact]
    public void Counter_SeparateSeriesByAttributes()
    {
        var agg = new MetricAggregator();
        agg.Record(MakePoint("requests.count", MetricType.Counter, 1, new[] { new OTelAttribute("method", "GET") }));
        agg.Record(MakePoint("requests.count", MetricType.Counter, 2, new[] { new OTelAttribute("method", "POST") }));
        agg.Record(MakePoint("requests.count", MetricType.Counter, 3, new[] { new OTelAttribute("method", "GET") }));

        var result = agg.SnapshotAndReset();
        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Value == 4.0); // GET: 1+3
        Assert.Contains(result, p => p.Value == 2.0); // POST: 2
    }

    [Fact]
    public void Gauge_LastValueWins()
    {
        var agg = new MetricAggregator();
        agg.Record(MakePoint("cpu.usage", MetricType.Gauge, 50.0, ts: 100));
        agg.Record(MakePoint("cpu.usage", MetricType.Gauge, 72.5, ts: 200));
        agg.Record(MakePoint("cpu.usage", MetricType.Gauge, 65.0, ts: 300));

        var result = agg.SnapshotAndReset();
        var point = Assert.Single(result, p => p.Name == "cpu.usage");
        Assert.Equal(65.0, point.Value);
        Assert.Equal(MetricType.Gauge, point.Type);
    }

    [Fact]
    public void Histogram_AggregatesCorrectly()
    {
        var agg = new MetricAggregator();
        agg.Record(MakePoint("latency", MetricType.Histogram, 10.0));
        agg.Record(MakePoint("latency", MetricType.Histogram, 50.0));
        agg.Record(MakePoint("latency", MetricType.Histogram, 100.0));
        agg.Record(MakePoint("latency", MetricType.Histogram, 500.0));
        agg.Record(MakePoint("latency", MetricType.Histogram, 1000.0));

        var result = agg.SnapshotAndReset();
        var point = Assert.Single(result, p => p.Name == "latency");
        Assert.Equal(1660.0, point.Value); // Sum
        Assert.Equal(MetricType.Histogram, point.Type);
        Assert.Contains(point.Attributes!, a => a.Key == "histogram.count" && Convert.ToInt64(a.Value) == 5);
        Assert.Contains(point.Attributes!, a => a.Key == "histogram.min" && (double)a.Value == 10.0);
        Assert.Contains(point.Attributes!, a => a.Key == "histogram.max" && (double)a.Value == 1000.0);
    }

    [Fact]
    public void Histogram_Percentiles_AreReasonable()
    {
        var agg = new MetricAggregator();
        for (int i = 1; i <= 100; i++)
            agg.Record(MakePoint("latency", MetricType.Histogram, i));

        var result = agg.SnapshotAndReset();
        var point = Assert.Single(result, p => p.Name == "latency");
        Assert.Equal(5050.0, point.Value);

        var p50 = Convert.ToInt64(point.Attributes!.First(a => a.Key == "histogram.p50").Value);
        var p90 = Convert.ToInt64(point.Attributes!.First(a => a.Key == "histogram.p90").Value);
        var p99 = Convert.ToInt64(point.Attributes!.First(a => a.Key == "histogram.p99").Value);
        Assert.InRange(p50, 45, 55);
        Assert.InRange(p90, 85, 95);
        Assert.InRange(p99, 95, 105);
    }

    [Fact]
    public void SnapshotAndReset_ClearsState()
    {
        var agg = new MetricAggregator();
        agg.Record(MakePoint("requests.count", MetricType.Counter, 5));
        agg.SnapshotAndReset();

        var result = agg.SnapshotAndReset();
        Assert.Empty(result); // No data recorded since last reset
    }

    [Fact]
    public void AttributeCanonicalization_SortsByKey()
    {
        var attrs = MetricAggregator.CanonicalizeMergedAttributes(null, new[]
        {
            new OTelAttribute("z_key", "last"),
            new OTelAttribute("a_key", "first"),
            new OTelAttribute("m_key", "middle"),
        });

        Assert.NotNull(attrs);
        Assert.Equal("a_key", attrs![0].Key);
        Assert.Equal("m_key", attrs[1].Key);
        Assert.Equal("z_key", attrs[2].Key);
    }

    [Fact]
    public void AttributeCanonicalization_InlineOverridesTemplate()
    {
        var template = new Templates.TemplateSnapshot
        {
            Name = "test",
            Attributes = new Dictionary<string, object>
            {
                ["region"] = "eu",
                ["method"] = "GET",
            }.ToFrozenDictionary()
        };

        var inline = new[] { new OTelAttribute("method", "POST") };

        var attrs = MetricAggregator.CanonicalizeMergedAttributes(template, inline);
        Assert.NotNull(attrs);
        Assert.Equal(2, attrs!.Length);
        Assert.Equal("POST", attrs.First(a => a.Key == "method").Value);
        Assert.Equal("eu", attrs.First(a => a.Key == "region").Value);
    }

    [Fact]
    public void LargeHistogramValues_DoNotThrow()
    {
        var agg = new MetricAggregator();
        agg.Record(MakePoint("big", MetricType.Histogram, 999_999_999_999.0));
        agg.Record(MakePoint("big", MetricType.Histogram, 1.0));

        var result = agg.SnapshotAndReset();
        var point = Assert.Single(result);
        Assert.Contains(point.Attributes!, a => a.Key == "histogram.count" && Convert.ToInt64(a.Value) == 2);
        Assert.Contains(point.Attributes!, a => a.Key == "histogram.min" && (double)a.Value == 1.0);
    }

    private static MetricPoint MakePoint(string name, MetricType type, double value, OTelAttribute[]? attrs = null, long ts = 0)
    {
        return new MetricPoint
        {
            Name = name,
            Type = type,
            Value = value,
            TimestampUnixNano = ts == 0 ? PipelineInstance.GetTimestampNano() : ts,
            Attributes = attrs,
        };
    }
}
