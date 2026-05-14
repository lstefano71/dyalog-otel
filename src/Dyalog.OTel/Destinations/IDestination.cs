namespace Dyalog.OTel.Destinations;

/// <summary>
/// Contract for all telemetry destinations. Implementations are internal
/// to the DLL (no runtime plugin loading). Each destination is owned by
/// a pipeline and called only from background consumer threads.
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
