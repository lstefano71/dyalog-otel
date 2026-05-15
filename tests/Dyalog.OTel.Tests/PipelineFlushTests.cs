using Dyalog.OTel.Channels;
using Dyalog.OTel.Config;
using Dyalog.OTel.Pipeline;
using Dyalog.OTel.Tests.Helpers;
using PipelineInstance = Dyalog.OTel.Pipeline.Pipeline;

namespace Dyalog.OTel.Tests;

public class PipelineFlushTests
{
    [Fact]
    public void Flush_DrainsAllEnqueuedLogs()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(
            new List<Dyalog.OTel.Destinations.IDestination> { dest },
            new BatchConfig());

        pipeline.Start();

        // Enqueue items
        for (int i = 0; i < 10; i++)
        {
            pipeline.TryEnqueueLog(new LogRecord
            {
                TimestampUnixNano = PipelineInstance.GetTimestampNano(),
                SeverityNumber = 9,
                SeverityText = "INFO",
                Body = $"message {i}"
            });
        }

        pipeline.Flush();

        lock (dest.Logs)
        {
            Assert.Equal(10, dest.Logs.Count);
        }

        pipeline.Shutdown();
    }

    [Fact]
    public void Flush_SecondBatchAppearsAfterSecondFlush()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(
            new List<Dyalog.OTel.Destinations.IDestination> { dest },
            new BatchConfig());

        pipeline.Start();

        // First batch
        for (int i = 0; i < 5; i++)
        {
            pipeline.TryEnqueueLog(new LogRecord
            {
                TimestampUnixNano = PipelineInstance.GetTimestampNano(),
                SeverityNumber = 9,
                SeverityText = "INFO",
                Body = $"batch1-{i}"
            });
        }

        pipeline.Flush();

        int countAfterFirst;
        lock (dest.Logs)
        {
            countAfterFirst = dest.Logs.Count;
        }
        Assert.Equal(5, countAfterFirst);

        // Second batch
        for (int i = 0; i < 3; i++)
        {
            pipeline.TryEnqueueLog(new LogRecord
            {
                TimestampUnixNano = PipelineInstance.GetTimestampNano(),
                SeverityNumber = 9,
                SeverityText = "INFO",
                Body = $"batch2-{i}"
            });
        }

        pipeline.Flush();

        lock (dest.Logs)
        {
            Assert.Equal(8, dest.Logs.Count);
        }

        pipeline.Shutdown();
    }

    [Fact]
    public void Flush_DrainsPartialBatchesHeldByConsumers()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(
            new List<Dyalog.OTel.Destinations.IDestination> { dest },
            new BatchConfig
            {
                LogSize = 10_000,
                LogIntervalMs = 60_000,
                SpanSize = 10_000,
                SpanIntervalMs = 60_000,
                MetricSize = 10_000,
                MetricIntervalMs = 60_000
            });

        pipeline.Start();

        for (int i = 0; i < 100; i++)
        {
            pipeline.TryEnqueueLog(new LogRecord
            {
                TimestampUnixNano = PipelineInstance.GetTimestampNano(),
                SeverityNumber = 9,
                SeverityText = "INFO",
                Body = $"message {i}"
            });
        }

        for (int i = 0; i < 40; i++)
        {
            int span = pipeline.StartSpan($"span {i}", 0, null);
            pipeline.EndSpan(span, null, null);
            pipeline.TryEnqueueMetric(new MetricPoint
            {
                TimestampUnixNano = PipelineInstance.GetTimestampNano(),
                Name = "test.counter",
                Value = i,
                Type = MetricType.Counter
            });
        }

        Thread.Sleep(100);
        pipeline.Flush();

        lock (dest.Logs)
            Assert.Equal(100, dest.Logs.Count);
        lock (dest.Spans)
            Assert.Equal(40, dest.Spans.Count);
        lock (dest.Metrics)
            Assert.Equal(40, dest.Metrics.Count);

        pipeline.Shutdown();
    }
}
