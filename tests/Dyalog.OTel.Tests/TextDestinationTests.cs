using Dyalog.OTel.Channels;
using Dyalog.OTel.Destinations;
using Dyalog.OTel.Tests.Helpers;

namespace Dyalog.OTel.Tests;

public class TextDestinationTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"dyalog-otel-text-{Guid.NewGuid():N}");

    [Fact]
    public void WriteLogs_WritesHumanReadableTextLine()
    {
        Directory.CreateDirectory(_tempRoot);
        string path = Path.Combine(_tempRoot, "app.log");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var destination = new TextDestination(new TextDestinationOptions
        {
            Path = path,
            Rotation = RotationPeriod.None,
            AcceptLogs = true,
            PromotedAttributes = [new TextPromotedAttribute("process.pid", "Process ID")]
        }, clock);

        destination.Init();
        destination.WriteLogs(
        [
            new LogRecord
            {
                TimestampUnixNano = ToUnixNano(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
                SeverityNumber = 9,
                SeverityText = "INFO",
                Body = "Started",
                Attributes =
                [
                    new OTelAttribute("emitter", "SRV"),
                    new OTelAttribute("process.pid", 2760)
                ]
            }
        ]);
        destination.Shutdown();

        string text = File.ReadAllText(path);
        Assert.Contains("2026-01-02T03:04:05.0000000Z SRV Started Process ID: 2760", text);
    }

    [Fact]
    public void Flush_DoesNotEmitPartialSummary_ButShutdownDoes()
    {
        Directory.CreateDirectory(_tempRoot);
        string path = Path.Combine(_tempRoot, "summary.log");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 12, 10, 0, TimeSpan.Zero));
        var destination = new TextDestination(new TextDestinationOptions
        {
            Path = path,
            Rotation = RotationPeriod.None,
            AcceptLogs = true,
            AcceptMetricSummaries = true,
            SummaryRules =
            [
                new TextSummaryMetricRule("request.latency", TimeSpan.FromHours(1), [50d, 90d, 95d, 99d], IncludeBars: false)
            ]
        }, clock);

        destination.Init();
        ((IRawHistogramObservationDestination)destination).WriteRawHistogramMetrics(
        [
            new MetricPoint
            {
                Name = "request.latency",
                Value = 42,
                Type = MetricType.Histogram,
                TimestampUnixNano = ToUnixNano(new DateTimeOffset(2026, 1, 2, 12, 10, 0, TimeSpan.Zero)),
                Attributes = [new OTelAttribute("emitter", "CAL")]
            }
        ]);

        destination.Flush();
        long sizeAfterFlush = new FileInfo(path).Length;
        Assert.Equal(0, sizeAfterFlush);

        clock.SetUtcNow(new DateTimeOffset(2026, 1, 2, 12, 45, 0, TimeSpan.Zero));
        destination.Shutdown();

        string afterShutdown = File.ReadAllText(path);
        Assert.True(afterShutdown.Length > sizeAfterFlush);
        Assert.Contains("Final partial last hour for request.latency:", afterShutdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CAL", afterShutdown);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static long ToUnixNano(DateTimeOffset timestampUtc) =>
        (timestampUtc.UtcDateTime.Ticks - DateTime.UnixEpoch.Ticks) * 100;
}
