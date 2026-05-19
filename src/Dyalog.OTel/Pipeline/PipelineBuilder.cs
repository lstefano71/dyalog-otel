using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;

namespace Dyalog.OTel.Pipeline;

/// <summary>
/// Incremental builder for pipeline configuration.
/// Used in explicit init mode: otel_init → builder calls → otel_start.
/// </summary>
public sealed class PipelineBuilder
{
    private readonly OTelConfig _config;

    public PipelineBuilder()
    {
        _config = ConfigLoader.Load(); // Start with file + env defaults
    }

    public void AddDestination(string type, Dictionary<string, string> properties, HashSet<string>? signals = null)
    {
        var dest = new DestinationConfig
        {
            Type = type,
            Properties = properties,
            Signals = signals ?? new(StringComparer.OrdinalIgnoreCase) { "log", "span", "metric" }
        };
        _config.Destinations.Add(dest);
    }

    public void SetResource(string key, string value)
    {
        _config.Resource[key] = value;
    }

    public void SetBatchConfig(string signalType, int maxSize, int intervalMs)
    {
        switch (signalType.ToLowerInvariant())
        {
            case "log":
                _config.Batch.LogSize = maxSize;
                _config.Batch.LogIntervalMs = intervalMs;
                break;
            case "span":
                _config.Batch.SpanSize = maxSize;
                _config.Batch.SpanIntervalMs = intervalMs;
                break;
            case "metric":
                _config.Batch.MetricSize = maxSize;
                _config.Batch.MetricIntervalMs = intervalMs;
                break;
        }
    }

    internal Pipeline Build()
    {
        var destinations = new List<IDestination>();
        foreach (var destConfig in _config.Destinations)
        {
            var dest = DestinationFactory.Create(destConfig);
            if (dest != null)
                destinations.Add(dest);
        }

        if (destinations.Count == 0)
            destinations.Add(new ConsoleDestination());

        var pipeline = new Pipeline(destinations, _config.Batch);
        var autoDetected = Resources.ResourceDetector.Detect();
        pipeline.Resource = Resources.ResourceDetector.Merge(autoDetected, _config.Resource);
        pipeline.DefaultEmitter = _config.DefaultEmitter;
        pipeline.DefaultEmitterVersion = _config.DefaultEmitterVersion;
        foreach (var (name, version) in _config.EmitterRegistry)
            pipeline.EmitterRegistry[name] = version;

        foreach (var dest in destinations)
        {
            if (dest is IResourceAwareDestination resourceAware)
                resourceAware.SetResource(pipeline.Resource);
        }

        pipeline.ApplyEmitterConfigToDestinations(bestEffort: false);
        return pipeline;
    }
}
