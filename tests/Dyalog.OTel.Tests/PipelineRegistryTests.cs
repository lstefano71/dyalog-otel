using Dyalog.OTel.Pipeline;
using PipelineInstance = Dyalog.OTel.Pipeline.Pipeline;

namespace Dyalog.OTel.Tests;

public class PipelineRegistryTests
{
    [Fact]
    public void GetOrCreateSingleton_ConcurrentAccess_ReturnsSameInstance()
    {
        const int threadCount = 16;
        var barrier = new ManualResetEventSlim(false);
        var results = new PipelineInstance?[threadCount];
        var threads = new Thread[threadCount];

        for (int i = 0; i < threadCount; i++)
        {
            int index = i;
            threads[i] = new Thread(() =>
            {
                barrier.Wait();
                results[index] = PipelineRegistry.Resolve(0);
            })
            { IsBackground = true };
            threads[i].Start();
        }

        // Release all threads simultaneously
        barrier.Set();

        foreach (var t in threads)
            t.Join(TimeSpan.FromSeconds(10));

        // All threads should have received the same instance
        var first = results[0];
        Assert.NotNull(first);
        for (int i = 1; i < threadCount; i++)
        {
            Assert.True(ReferenceEquals(first, results[i]));
        }

        // Clean up
        PipelineRegistry.Shutdown(0);
    }
}
