using Dyalog.OTel.Pipeline;
using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;
using System.Reflection;
using PipelineInstance = Dyalog.OTel.Pipeline.Pipeline;

public class SpanHandleTests
{
    [Fact]
    public void HandleNeverReturnsZero()
    {
        var dest = new ConsoleDestination();
        var pipeline = new PipelineInstance(new List<IDestination> { dest }, new BatchConfig());
        pipeline.Start();

        // Use reflection to set _nextSpanHandle close to overflow
        var field = typeof(PipelineInstance).GetField("_nextSpanHandle", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(pipeline, int.MaxValue);

        int h1 = pipeline.StartSpan("test1", 0, null);
        Assert.True(h1 > 0, $"Handle should be positive, got {h1}");

        int h2 = pipeline.StartSpan("test2", 0, null);
        Assert.True(h2 > 0, $"Handle after wrap should be positive, got {h2}");

        // Clean up
        pipeline.EndSpan(h1, null, null);
        pipeline.EndSpan(h2, null, null);
        pipeline.Shutdown();
    }
}
