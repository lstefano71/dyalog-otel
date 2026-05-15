using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;

namespace Dyalog.OTel.Tests;

public class TextDestinationConfigTests
{
    [Fact]
    public void Parse_RequiresExplicitPath()
    {
        var config = new DestinationConfig
        {
            Type = "text",
            Properties = new Dictionary<string, string>()
        };

        Assert.Throws<InvalidOperationException>(() => TextDestinationConfig.Parse(config));
    }

    [Fact]
    public void Parse_BuildsPromotionsAndSummaryRules()
    {
        var config = new DestinationConfig
        {
            Type = "text",
            Signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "log", "metric" },
            Properties = new Dictionary<string, string>
            {
                ["path"] = "logs/app.log",
                ["rotate"] = "daily",
                ["startup"] = "true",
                ["promote"] = "process.pid, apl.version",
                ["label.process.pid"] = "Process ID",
                ["summary.interval"] = "1h",
                ["summary.percentiles"] = "50,90,95,99",
                ["summary.metric.latency.name"] = "request.latency",
                ["summary.metric.latency.bars"] = "true",
                ["summary.metric.latency.percentiles"] = "75,99"
            }
        };

        var options = TextDestinationConfig.Parse(config);

        Assert.True(Path.IsPathFullyQualified(options.Path));
        Assert.Equal(RotationPeriod.Daily, options.Rotation);
        Assert.True(options.EmitStartupBlock);
        Assert.True(options.AcceptLogs);
        Assert.True(options.AcceptMetricSummaries);

        Assert.Collection(options.PromotedAttributes,
            attr =>
            {
                Assert.Equal("process.pid", attr.Key);
                Assert.Equal("Process ID", attr.Label);
            },
            attr =>
            {
                Assert.Equal("apl.version", attr.Key);
                Assert.Equal("apl.version", attr.Label);
            });

        var rule = Assert.Single(options.SummaryRules);
        Assert.Equal("request.latency", rule.Name);
        Assert.Equal(TimeSpan.FromHours(1), rule.Interval);
        Assert.True(rule.IncludeBars);
        Assert.Equal(new[] { 75d, 99d }, rule.Percentiles);
    }

    [Fact]
    public void Parse_RejectsSummariesWithoutMetricSignal()
    {
        var config = new DestinationConfig
        {
            Type = "text",
            Signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "log" },
            Properties = new Dictionary<string, string>
            {
                ["path"] = "logs/app.log",
                ["summary.metric.latency.name"] = "request.latency"
            }
        };

        Assert.Throws<InvalidOperationException>(() => TextDestinationConfig.Parse(config));
    }
}
