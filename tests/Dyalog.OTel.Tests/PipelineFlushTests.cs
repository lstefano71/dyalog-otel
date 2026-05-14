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
}
