using Dyalog.OTel.Pipeline;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;
using PipelineInstance = Dyalog.OTel.Pipeline.Pipeline;

public class IdGenerationTests
{
    [Fact]
    public void SpanIds_HaveCorrectLength()
    {
        // Create a minimal pipeline to test ID generation
        var dest = new ConsoleDestination();
        var pipeline = new PipelineInstance(new List<IDestination> { dest }, new BatchConfig());
        pipeline.Start();

        int handle = pipeline.StartSpan("test", 0, null);
        var ctx = pipeline.GetSpanContext(handle);

        Assert.NotNull(ctx);
        Assert.Equal(16, ctx!.Value.TraceId.Length); // trace ID = 16 bytes
        Assert.Equal(8, ctx!.Value.SpanId.Length);   // span ID = 8 bytes

        // Verify not all zeros
        Assert.False(ctx.Value.TraceId.All(b => b == 0));
        Assert.False(ctx.Value.SpanId.All(b => b == 0));

        pipeline.Shutdown();
    }
}
