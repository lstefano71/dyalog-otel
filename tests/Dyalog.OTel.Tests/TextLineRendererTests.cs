using Dyalog.OTel.Channels;
using Dyalog.OTel.Destinations;

namespace Dyalog.OTel.Tests;

public class TextLineRendererTests
{
    [Fact]
    public void RenderLog_FormatsWarningAndPromotedAttributes()
    {
        var renderer = new TextLineRenderer(new TextDestinationOptions
        {
            Path = @"C:\logs\app.log",
            PromotedAttributes =
            [
                new TextPromotedAttribute("process.pid", "Process ID"),
                new TextPromotedAttribute("user", "User")
            ]
        });

        var lines = renderer.RenderLog(new LogRecord
        {
            TimestampUnixNano = ToUnixNano(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
            SeverityNumber = 13,
            SeverityText = "WARN",
            Body = "Cache miss",
            Attributes =
            [
                new OTelAttribute("emitter", "CAL"),
                new OTelAttribute("process.pid", 42),
                new OTelAttribute("user", "alice")
            ]
        }, "CAL");

        string line = Assert.Single(lines);
        Assert.Equal("2026-01-02T03:04:05Z CAL WARNING Cache miss Process ID: 42, User: alice", line);
    }

    [Fact]
    public void RenderLog_SplitsMultilineInfoMessages()
    {
        var renderer = new TextLineRenderer(new TextDestinationOptions
        {
            Path = @"C:\logs\app.log"
        });

        var lines = renderer.RenderLog(new LogRecord
        {
            TimestampUnixNano = ToUnixNano(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
            SeverityNumber = 9,
            SeverityText = "INFO",
            Body = "First line\r\nSecond line",
            Attributes = [new OTelAttribute("emitter", "SRV")]
        }, "SRV");

        Assert.Equal(
        [
            "2026-01-02T03:04:05Z SRV First line",
            "2026-01-02T03:04:05Z SRV Second line"
        ], lines);
    }

    [Fact]
    public void RenderSummary_IncludesHeaderPercentilesAndBars()
    {
        var renderer = new TextLineRenderer(new TextDestinationOptions
        {
            Path = @"C:\logs\app.log"
        });

        var lines = renderer.RenderSummary(new TextSummaryBlock
        {
            TimestampUtc = new DateTimeOffset(2026, 1, 2, 4, 0, 0, TimeSpan.Zero),
            Emitter = "CAL",
            MetricName = "request.latency",
            Interval = TimeSpan.FromHours(1),
            IsPartial = false,
            Count = 3,
            Sum = 60,
            Min = 10,
            Max = 30,
            IncludeBars = true,
            Percentiles =
            [
                new TextPercentileValue(50, 20),
                new TextPercentileValue(99, 30)
            ],
            ExplicitBounds = [0, 25, 50],
            BucketCounts = [1, 2, 0, 0]
        });

        Assert.Contains("2026-01-02T04:00:00Z CAL Last hour for request.latency:", lines);
        Assert.Contains("2026-01-02T04:00:00Z CAL Percentiles: p50: 20, p99: 30", lines);
        Assert.Contains(lines, line => line.Contains("|"));
    }

    private static long ToUnixNano(DateTimeOffset timestampUtc) =>
        (timestampUtc.UtcDateTime.Ticks - DateTime.UnixEpoch.Ticks) * 100;
}
