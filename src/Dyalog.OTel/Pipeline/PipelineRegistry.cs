using System.Collections.Concurrent;
using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;
using Dyalog.OTel.Resources;

namespace Dyalog.OTel.Pipeline;

/// <summary>
/// Global registry of pipelines. Handle 0 is the lazy singleton.
/// Thread-safe for handle lookup (background threads read, interpreter thread writes).
/// </summary>
public static class PipelineRegistry
{
    private static Pipeline? _singleton;
    private static readonly ConcurrentDictionary<int, Pipeline> _pipelines = new();
    private static int _nextHandle = 1;

    private static volatile PipelineState _singletonState = PipelineState.Unconfigured;

    /// <summary>
    /// Get or lazy-init the singleton pipeline (handle 0).
    /// </summary>
    public static Pipeline GetOrCreateSingleton()
    {
        if (_singleton != null && _singletonState == PipelineState.Running)
            return _singleton;

        if (_singletonState == PipelineState.Unconfigured)
        {
            // Lazy init: load config, create pipeline, start
            var config = ConfigLoader.Load();
            _singleton = BuildPipeline(config);
            _singletonState = PipelineState.Running;
            _singleton.Start();
        }

        return _singleton!;
    }

    /// <summary>Begin configuring the singleton (explicit init mode).</summary>
    public static PipelineBuilder InitSingleton()
    {
        _singletonState = PipelineState.Configuring;
        return new PipelineBuilder();
    }

    /// <summary>Freeze config and start the singleton.</summary>
    public static void StartSingleton(PipelineBuilder builder)
    {
        _singleton = builder.Build();
        _singletonState = PipelineState.Running;
        _singleton.Start();
    }

    /// <summary>Create an explicit pipeline with a new handle.</summary>
    public static int CreatePipeline(OTelConfig config)
    {
        var pipeline = BuildPipeline(config);
        int handle = Interlocked.Increment(ref _nextHandle);
        _pipelines[handle] = pipeline;
        pipeline.Start();
        return handle;
    }

    /// <summary>Resolve a pipeline by handle. 0 = singleton.</summary>
    public static Pipeline? Resolve(int handle)
    {
        if (handle == 0) return GetOrCreateSingleton();
        _pipelines.TryGetValue(handle, out var pipeline);
        return pipeline;
    }

    /// <summary>Shutdown and remove a pipeline.</summary>
    public static void Shutdown(int handle)
    {
        if (handle == 0)
        {
            _singleton?.Shutdown();
            _singleton = null;
            _singletonState = PipelineState.Unconfigured;
            return;
        }

        if (_pipelines.TryRemove(handle, out var pipeline))
            pipeline.Shutdown();
    }

    /// <summary>Flush a pipeline's channels.</summary>
    public static void Flush(int handle)
    {
        var pipeline = Resolve(handle);
        pipeline?.Flush();
    }

    private static Pipeline BuildPipeline(OTelConfig config)
    {
        var destinations = new List<IDestination>();

        foreach (var destConfig in config.Destinations)
        {
            var dest = DestinationFactory.Create(destConfig);
            if (dest != null)
                destinations.Add(dest);
        }

        // If no destinations configured, add a console fallback
        if (destinations.Count == 0)
            destinations.Add(new ConsoleDestination());

        var pipeline = new Pipeline(destinations, config.Batch);

        // Set resource attributes
        var autoDetected = ResourceDetector.Detect();
        pipeline.Resource = ResourceDetector.Merge(autoDetected, config.Resource);

        return pipeline;
    }

    private enum PipelineState
    {
        Unconfigured,
        Configuring,
        Running
    }
}
