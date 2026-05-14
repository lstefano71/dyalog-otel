using System.Threading;

namespace Dyalog.OTel.Diagnostics;

/// <summary>
/// Atomic counters tracking pipeline health. Readable via otel_stats.
/// All operations are lock-free (Interlocked).
/// </summary>
public sealed class InternalMetrics
{
    // Per-signal counters
    private long _logsEnqueued;
    private long _logsExported;
    private long _logsDropped;

    private long _spansEnqueued;
    private long _spansExported;
    private long _spansDropped;

    private long _metricsEnqueued;
    private long _metricsExported;
    private long _metricsDropped;

    private long _exportErrors;

    // Log counters
    public void LogEnqueued() => Interlocked.Increment(ref _logsEnqueued);
    public void LogExported(int count) => Interlocked.Add(ref _logsExported, count);
    public void LogDropped() => Interlocked.Increment(ref _logsDropped);

    // Span counters
    public void SpanEnqueued() => Interlocked.Increment(ref _spansEnqueued);
    public void SpanExported(int count) => Interlocked.Add(ref _spansExported, count);
    public void SpanDropped() => Interlocked.Increment(ref _spansDropped);

    // Metric counters
    public void MetricEnqueued() => Interlocked.Increment(ref _metricsEnqueued);
    public void MetricExported(int count) => Interlocked.Add(ref _metricsExported, count);
    public void MetricDropped() => Interlocked.Increment(ref _metricsDropped);

    // Export errors
    public void ExportError() => Interlocked.Increment(ref _exportErrors);

    /// <summary>Returns a snapshot of all counters.</summary>
    public MetricsSnapshot GetSnapshot() => new()
    {
        LogsEnqueued = Interlocked.Read(ref _logsEnqueued),
        LogsExported = Interlocked.Read(ref _logsExported),
        LogsDropped = Interlocked.Read(ref _logsDropped),
        SpansEnqueued = Interlocked.Read(ref _spansEnqueued),
        SpansExported = Interlocked.Read(ref _spansExported),
        SpansDropped = Interlocked.Read(ref _spansDropped),
        MetricsEnqueued = Interlocked.Read(ref _metricsEnqueued),
        MetricsExported = Interlocked.Read(ref _metricsExported),
        MetricsDropped = Interlocked.Read(ref _metricsDropped),
        ExportErrors = Interlocked.Read(ref _exportErrors)
    };
}

public readonly record struct MetricsSnapshot
{
    public long LogsEnqueued { get; init; }
    public long LogsExported { get; init; }
    public long LogsDropped { get; init; }
    public long SpansEnqueued { get; init; }
    public long SpansExported { get; init; }
    public long SpansDropped { get; init; }
    public long MetricsEnqueued { get; init; }
    public long MetricsExported { get; init; }
    public long MetricsDropped { get; init; }
    public long ExportErrors { get; init; }
}
