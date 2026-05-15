using System.Threading;

namespace Dyalog.OTel.Diagnostics;

/// <summary>
/// Counters tracking pipeline health. Readable via otel_stats.
/// Export-side counters are updated atomically by background threads; enqueue/drop
/// counters are written only by the interpreter thread and read with volatile loads.
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

    // Interpreter-thread counters
    public void LogEnqueued() => _logsEnqueued++;
    public void LogDropped() => _logsDropped++;
    public void SpanEnqueued() => _spansEnqueued++;
    public void SpanDropped() => _spansDropped++;
    public void MetricEnqueued() => _metricsEnqueued++;
    public void MetricDropped() => _metricsDropped++;

    // Background-thread counters
    public void LogExported(int count) => Interlocked.Add(ref _logsExported, count);
    public void SpanExported(int count) => Interlocked.Add(ref _spansExported, count);
    public void MetricExported(int count) => Interlocked.Add(ref _metricsExported, count);

    // Export errors
    public void ExportError() => Interlocked.Increment(ref _exportErrors);

    /// <summary>Returns a snapshot of all counters.</summary>
    public MetricsSnapshot GetSnapshot() => new()
    {
        LogsEnqueued = Volatile.Read(ref _logsEnqueued),
        LogsExported = Interlocked.Read(ref _logsExported),
        LogsDropped = Volatile.Read(ref _logsDropped),
        SpansEnqueued = Volatile.Read(ref _spansEnqueued),
        SpansExported = Interlocked.Read(ref _spansExported),
        SpansDropped = Volatile.Read(ref _spansDropped),
        MetricsEnqueued = Volatile.Read(ref _metricsEnqueued),
        MetricsExported = Interlocked.Read(ref _metricsExported),
        MetricsDropped = Volatile.Read(ref _metricsDropped),
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
