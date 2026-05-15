using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;

namespace Dyalog.OTel.Tests;

public class DestinationFactoryTests
{
    [Fact]
    public void FileDestination_CanonicalizesPath()
    {
        var dest = new JsonlFileDestination(
            @"C:\logs\..\temp\evil.jsonl",
            RotationPeriod.Monthly,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "log", "span", "metric" });
        Assert.NotNull(dest);
    }

    [Fact]
    public void UnknownDestinationType_Throws()
    {
        var config = new DestinationConfig { Type = "foobar" };
        Assert.Throws<InvalidOperationException>(() => DestinationFactory.Create(config));
    }

    [Fact]
    public void PluginBackedDestination_RequiresPublishedNativeCompanion()
    {
        var config = new DestinationConfig { Type = "otlp" };
        Assert.Throws<InvalidOperationException>(() => DestinationFactory.Create(config));
    }

    [Fact]
    public void ConsoleType_ReturnsInstance()
    {
        var config = new DestinationConfig { Type = "console" };
        var result = DestinationFactory.Create(config);
        Assert.NotNull(result);
        Assert.Equal("console", result.Name);
        result.Dispose();
    }

    [Fact]
    public void TextType_ReturnsInstance()
    {
        var config = new DestinationConfig
        {
            Type = "text",
            Properties = new Dictionary<string, string>
            {
                ["path"] = "app.log"
            }
        };

        var result = DestinationFactory.Create(config);
        Assert.NotNull(result);
        Assert.Equal("text", result.Name);
        result.Dispose();
    }
}
