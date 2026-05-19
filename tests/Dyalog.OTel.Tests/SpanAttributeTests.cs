using Dyalog.OTel.Pipeline;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;
using Dyalog.OTel.Tests.Helpers;
using PipelineInstance = Dyalog.OTel.Pipeline.Pipeline;

namespace Dyalog.OTel.Tests;

public class SpanAttributeTests
{
    [Fact]
    public void StartAttributes_AppearInSpanRecord()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(new List<IDestination> { dest }, new BatchConfig());
        pipeline.Start();

        var startAttrs = new OTelAttribute[] { new("region", "us-east") };
        int handle = pipeline.StartSpan("test-op", 0, null, null, startAttrs);
        pipeline.EndSpan(handle, null, null);

        pipeline.Flush();
        Thread.Sleep(200); // Give consumer time

        Assert.Single(dest.Spans);
        var span = dest.Spans[0];
        Assert.NotNull(span.Attributes);
        Assert.Contains(span.Attributes, a => a.Key == "region" && a.Value.Equals("us-east"));

        pipeline.Shutdown();
    }

    [Fact]
    public void EndAttributes_OverrideStartAttributes()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(new List<IDestination> { dest }, new BatchConfig());
        pipeline.Start();

        var startAttrs = new OTelAttribute[] { new("status", "pending"), new("region", "us-east") };
        var endAttrs = new OTelAttribute[] { new("status", "complete") };

        int handle = pipeline.StartSpan("test-op", 0, null, null, startAttrs);
        pipeline.EndSpan(handle, endAttrs, null);

        pipeline.Flush();
        Thread.Sleep(200);

        Assert.Single(dest.Spans);
        var span = dest.Spans[0];
        Assert.NotNull(span.Attributes);
        // "status" should be overridden to "complete"
        var statusAttr = span.Attributes!.First(a => a.Key == "status");
        Assert.Equal("complete", statusAttr.Value);
        // "region" should still be present from start
        Assert.Contains(span.Attributes, a => a.Key == "region");

        pipeline.Shutdown();
    }

    [Fact]
    public void NoStartAttributes_StillWorks()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(new List<IDestination> { dest }, new BatchConfig());
        pipeline.Start();

        var endAttrs = new OTelAttribute[] { new("result", "ok") };
        int handle = pipeline.StartSpan("simple-op", 0, null);
        pipeline.EndSpan(handle, endAttrs, null);

        pipeline.Flush();
        Thread.Sleep(200);

        Assert.Single(dest.Spans);
        var span = dest.Spans[0];
        Assert.NotNull(span.Attributes);
        Assert.Contains(span.Attributes, a => a.Key == "result" && a.Value.Equals("ok"));

        pipeline.Shutdown();
    }

    [Fact]
    public void StartTemplate_UsedWhenNoEndTemplate()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(new List<IDestination> { dest }, new BatchConfig());
        pipeline.Start();        // Register a template
        pipeline.Templates.Create("my-tpl", new Dictionary<string, object>
        {
            ["service"] = "web"
        });

        var tpl = pipeline.Templates.TryGet("my-tpl");
        int handle = pipeline.StartSpan("tpl-op", 0, null, null, null, tpl);
        pipeline.EndSpan(handle, null, null); // No end template

        pipeline.Flush();
        Thread.Sleep(200);

        Assert.Single(dest.Spans);
        var span = dest.Spans[0];
        Assert.NotNull(span.Template);
        Assert.Equal("my-tpl", span.Template!.Name);

        pipeline.Shutdown();
    }

    [Fact]
    public void ExternalTraceId_WithParentHandle_KeepsParentSpanId()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(new List<IDestination> { dest }, new BatchConfig());
        pipeline.Start();

        int parentHandle = pipeline.StartSpan("parent", 0, null);
        var parentContext = pipeline.GetSpanContext(parentHandle);
        Assert.NotNull(parentContext);

        byte[] externalTraceId = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        int childHandle = pipeline.StartSpan("child", parentHandle, externalTraceId);
        pipeline.EndSpan(childHandle, null, null);
        pipeline.EndSpan(parentHandle, null, null);

        pipeline.Flush();
        Thread.Sleep(200);

        var child = Assert.Single(dest.Spans, span => span.Name == "child");
        Assert.Equal(externalTraceId, child.TraceId);
        Assert.Equal(parentContext.Value.SpanId, child.ParentSpanId);

        pipeline.Shutdown();
    }
}
