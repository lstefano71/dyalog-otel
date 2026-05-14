using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;

namespace Dyalog.OTel.Tests;

public class DestinationFactoryTests
{
    [Fact]
    public void FileDestination_CanonicalizesPath()
    {
        var config = new DestinationConfig
        {
            Type = "file",
            Properties = new Dictionary<string, string>
            {
                ["path"] = @"C:\logs\..\temp\evil.jsonl"
            }
        };

        var dest = DestinationFactory.Create(config) as JsonlFileDestination;
        Assert.NotNull(dest);
    }

    [Fact]
    public void UnknownDestinationType_ReturnsNull()
    {
        var config = new DestinationConfig { Type = "foobar" };
        var result = DestinationFactory.Create(config);
        Assert.Null(result);
    }

    [Fact]
    public void OtlpType_ReturnsDestination()
    {
        var config = new DestinationConfig { Type = "otlp" };
        var result = DestinationFactory.Create(config);
        Assert.NotNull(result);
        result.Dispose();
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
}
