using System.Collections.Concurrent;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Destinations;

namespace Dyalog.OTel.Tests.Helpers;

internal class TestDestination : IDestination
{
    public List<LogRecord> Logs = new();
    public List<SpanRecord> Spans = new();
    public List<MetricPoint> Metrics = new();

    public string Name => "test";

    public void Init() { }

    public void WriteLogs(ReadOnlySpan<LogRecord> batch)
    {
        lock (Logs)
            foreach (var r in batch)
                Logs.Add(r);
    }

    public void WriteSpans(ReadOnlySpan<SpanRecord> batch)
    {
        lock (Spans)
            foreach (var r in batch)
                Spans.Add(r);
    }

    public void WriteMetrics(ReadOnlySpan<MetricPoint> batch)
    {
        lock (Metrics)
            foreach (var r in batch)
                Metrics.Add(r);
    }

    public void Flush() { }
    public void Shutdown() { }
    public void Dispose() { }
}

/// <summary>
/// A test destination that also captures emitter config updates,
/// used to verify late emitter registration propagation.
/// </summary>
internal class TestEmitterAwareDestination : IDestination, IEmitterAwareDestination
{
    public List<LogRecord> Logs = new();
    public List<SpanRecord> Spans = new();
    public List<MetricPoint> Metrics = new();

    public string? LastDefaultEmitter { get; private set; }
    public string? LastDefaultEmitterVersion { get; private set; }
    public ConcurrentDictionary<string, string>? LastRegistry { get; private set; }
    public int SetEmitterConfigCallCount { get; private set; }

    public string Name => "test-emitter-aware";

    public void SetEmitterConfig(string defaultEmitter, string defaultEmitterVersion, ConcurrentDictionary<string, string> registry)
    {
        LastDefaultEmitter = defaultEmitter;
        LastDefaultEmitterVersion = defaultEmitterVersion;
        LastRegistry = registry;
        SetEmitterConfigCallCount++;
    }

    public void Init() { }

    public void WriteLogs(ReadOnlySpan<LogRecord> batch)
    {
        lock (Logs) foreach (var r in batch) Logs.Add(r);
    }

    public void WriteSpans(ReadOnlySpan<SpanRecord> batch)
    {
        lock (Spans) foreach (var r in batch) Spans.Add(r);
    }

    public void WriteMetrics(ReadOnlySpan<MetricPoint> batch)
    {
        lock (Metrics) foreach (var r in batch) Metrics.Add(r);
    }

    public void Flush() { }
    public void Shutdown() { }
    public void Dispose() { }
}
