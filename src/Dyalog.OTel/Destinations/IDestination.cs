namespace Dyalog.OTel.Destinations;

/// <summary>
/// Contract for all telemetry destinations. Implementations may live in
/// the core DLL or in companion DLLs, but each destination is still owned
/// by a pipeline and called only from background consumer threads.
/// </summary>
public interface IDestination : IDisposable
{
    /// <summary>Destination type name (for diagnostics).</summary>
    string Name { get; }

    /// <summary>Initialize the destination (open files, create HttpClient, etc.).</summary>
    void Init();

    /// <summary>Write a batch of log records.</summary>
    void WriteLogs(ReadOnlySpan<Channels.LogRecord> batch);

    /// <summary>Write a batch of span records.</summary>
    void WriteSpans(ReadOnlySpan<Channels.SpanRecord> batch);

    /// <summary>Write a batch of metric points.</summary>
    void WriteMetrics(ReadOnlySpan<Channels.MetricPoint> batch);

    /// <summary>Flush any buffered data to the underlying transport.</summary>
    void Flush();

    /// <summary>Graceful shutdown — flush and release resources.</summary>
    void Shutdown();
}

public interface IResourceAwareDestination
{
    void SetResource(Dictionary<string, string> resource);
}

/// <summary>
/// Destinations that need emitter/scope info for grouping signals by InstrumentationScope.
/// </summary>
public interface IEmitterAwareDestination
{
    void SetEmitterConfig(string defaultEmitter, string defaultEmitterVersion, System.Collections.Concurrent.ConcurrentDictionary<string, string> registry);
}

/// <summary>
/// Marker for destinations that need raw histogram observations in addition to
/// the pipeline's aggregated histogram export path.
/// </summary>
public interface IRawHistogramObservationDestination
{
    void WriteRawHistogramMetrics(ReadOnlySpan<Channels.MetricPoint> batch);
}
