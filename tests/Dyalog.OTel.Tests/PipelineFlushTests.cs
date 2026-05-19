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

    [Fact]
    public void EmergencyDrain_DrainsPartialBatchesHeldByConsumers()
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

        for (int i = 0; i < 5; i++)
        {
            pipeline.TryEnqueueLog(new LogRecord
            {
                TimestampUnixNano = PipelineInstance.GetTimestampNano(),
                SeverityNumber = 9,
                SeverityText = "INFO",
                Body = $"message {i}"
            });
            pipeline.TryEnqueueMetric(new MetricPoint
            {
                TimestampUnixNano = PipelineInstance.GetTimestampNano(),
                Name = "test.counter",
                Value = i,
                Type = MetricType.Counter
            });
        }

        Thread.Sleep(100);
        pipeline.EmergencyDrain();

        lock (dest.Logs)
            Assert.Equal(5, dest.Logs.Count);
        lock (dest.Metrics)
            Assert.Equal(5, dest.Metrics.Count);
    }

    [Fact]
    public void MetricInterval_ExportsHistogramSnapshots()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(
            new List<Dyalog.OTel.Destinations.IDestination> { dest },
            new BatchConfig
            {
                MetricSize = 10_000,
                MetricIntervalMs = 50
            });

        pipeline.Start();

        pipeline.TryEnqueueMetric(new MetricPoint
        {
            TimestampUnixNano = PipelineInstance.GetTimestampNano(),
            Name = "http.request.duration",
            Value = 0.42,
            Type = MetricType.Histogram
        });

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            lock (dest.Metrics)
            {
                if (dest.Metrics.Count > 0)
                    break;
            }
            Thread.Sleep(10);
        }

        lock (dest.Metrics)
        {
            var histogram = Assert.Single(dest.Metrics);
            Assert.Equal("http.request.duration", histogram.Name);
            Assert.Equal(MetricType.Histogram, histogram.Type);
            Assert.Contains(histogram.Attributes!, attr => attr.Key == "histogram.count" && Convert.ToInt64(attr.Value) == 1);
        }

        pipeline.Shutdown();
    }
}
