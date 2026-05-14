using System.Threading.Channels;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;
using Dyalog.OTel.Diagnostics;
using Dyalog.OTel.Templates;

namespace Dyalog.OTel.Pipeline;

/// <summary>
/// A telemetry pipeline: owns three typed channels (log/span/metric),
/// three consumer threads, and a set of destinations.
/// </summary>
public sealed class Pipeline : IDisposable
{
    private readonly Channel<LogRecord> _logChannel;
    private readonly Channel<SpanRecord> _spanChannel;
    private readonly Channel<MetricPoint> _metricChannel;

    private readonly Thread _logConsumer;
    private readonly Thread _spanConsumer;
    private readonly Thread _metricConsumer;

    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource _wakeCts = new(); // cancelled to interrupt consumer waits
    private readonly ManualResetEventSlim _logFlushed = new(false);
    private readonly ManualResetEventSlim _spanFlushed = new(false);
    private readonly ManualResetEventSlim _metricFlushed = new(false);

    private readonly List<IDestination> _destinations;
    private readonly BatchConfig _batchConfig;
    public InternalMetrics Metrics { get; } = new();
    public TemplateRegistry Templates { get; } = new();
    public Dictionary<string, string> Resource { get; set; } = new();

    // Span tracking for trace/span ID resolution on hot path
    private readonly Dictionary<int, ActiveSpan> _activeSpans = new();
    private int _nextSpanHandle = 1;
    private readonly object _spanLock = new();

    public Pipeline(List<IDestination> destinations, BatchConfig batchConfig, int channelCapacity = 8192)
    {
        _destinations = destinations;
        _batchConfig = batchConfig;

        var opts = new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropNewest,
            SingleReader = true,
            SingleWriter = true // APL interpreter is single-threaded
        };

        _logChannel = Channel.CreateBounded<LogRecord>(opts);
        _spanChannel = Channel.CreateBounded<SpanRecord>(opts);
        _metricChannel = Channel.CreateBounded<MetricPoint>(opts);

        _logConsumer = new Thread(() => ConsumeLoop(_logChannel.Reader, _batchConfig.LogSize, _batchConfig.LogIntervalMs, _logFlushed,
            (batch) => { foreach (var d in _destinations) d.WriteLogs(batch); Metrics.LogExported(batch.Length); }))
        { IsBackground = true, Name = "otel-log-consumer" };

        _spanConsumer = new Thread(() => ConsumeLoop(_spanChannel.Reader, _batchConfig.SpanSize, _batchConfig.SpanIntervalMs, _spanFlushed,
            (batch) => { foreach (var d in _destinations) d.WriteSpans(batch); Metrics.SpanExported(batch.Length); }))
        { IsBackground = true, Name = "otel-span-consumer" };

        _metricConsumer = new Thread(() => ConsumeLoop(_metricChannel.Reader, _batchConfig.MetricSize, _batchConfig.MetricIntervalMs, _metricFlushed,
            (batch) => { foreach (var d in _destinations) d.WriteMetrics(batch); Metrics.MetricExported(batch.Length); }))
        { IsBackground = true, Name = "otel-metric-consumer" };
    }

    public void Start()
    {
        foreach (var d in _destinations)
            d.Init();

        _logConsumer.Start();
        _spanConsumer.Start();
        _metricConsumer.Start();
    }

    // ── Hot-path enqueue methods (called from interpreter thread) ──

    public bool TryEnqueueLog(LogRecord record)
    {
        if (_logChannel.Writer.TryWrite(record))
        {
            Metrics.LogEnqueued();
            return true;
        }
        Metrics.LogDropped();
        return false;
    }

    public bool TryEnqueueMetric(MetricPoint point)
    {
        if (_metricChannel.Writer.TryWrite(point))
        {
            Metrics.MetricEnqueued();
            return true;
        }
        Metrics.MetricDropped();
        return false;
    }

    // ── Span management (called from interpreter thread) ──

    public int StartSpan(string name, int parentHandle, byte[]? traceId)
    {
        lock (_spanLock)
        {
            int handle = _nextSpanHandle++;
            byte[] spanId = GenerateId(8);
            byte[] resolvedTraceId = traceId ?? (parentHandle != 0 && _activeSpans.TryGetValue(parentHandle, out var parent)
                ? parent.TraceId
                : GenerateId(16));
            byte[]? parentSpanId = parentHandle != 0 && _activeSpans.TryGetValue(parentHandle, out var p)
                ? p.SpanId
                : null;

            _activeSpans[handle] = new ActiveSpan
            {
                TraceId = resolvedTraceId,
                SpanId = spanId,
                ParentSpanId = parentSpanId,
                Name = name,
                StartTimeUnixNano = GetTimestampNano()
            };
            return handle;
        }
    }

    public (byte[] TraceId, byte[] SpanId)? GetSpanContext(int spanHandle)
    {
        lock (_spanLock)
        {
            if (_activeSpans.TryGetValue(spanHandle, out var span))
                return (span.TraceId, span.SpanId);
            return null;
        }
    }

    public bool EndSpan(int spanHandle, OTelAttribute[]? attrs, Templates.TemplateSnapshot? template)
    {
        ActiveSpan span;
        lock (_spanLock)
        {
            if (!_activeSpans.Remove(spanHandle, out span!))
                return false;
        }

        var record = new SpanRecord
        {
            TraceId = span.TraceId,
            SpanId = span.SpanId,
            ParentSpanId = span.ParentSpanId,
            Name = span.Name,
            StartTimeUnixNano = span.StartTimeUnixNano,
            EndTimeUnixNano = GetTimestampNano(),
            Template = template,
            Attributes = attrs
        };

        if (_spanChannel.Writer.TryWrite(record))
        {
            Metrics.SpanEnqueued();
            return true;
        }
        Metrics.SpanDropped();
        return false;
    }

    private volatile int _flushRequested; // 0 = no, 1 = yes

    // ── Flush / Shutdown ──

    public void Flush()
    {
        _logFlushed.Reset();
        _spanFlushed.Reset();
        _metricFlushed.Reset();

        // Signal consumers to drain and report
        Volatile.Write(ref _flushRequested, 1);

        // Wake consumers that are blocked in WaitToReadAsync
        var old = Interlocked.Exchange(ref _wakeCts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();

        _logFlushed.Wait(TimeSpan.FromSeconds(30));
        _spanFlushed.Wait(TimeSpan.FromSeconds(30));
        _metricFlushed.Wait(TimeSpan.FromSeconds(30));

        // Clear the flag so consumers resume normal operation
        Volatile.Write(ref _flushRequested, 0);

        foreach (var d in _destinations)
            d.Flush();
    }

    public void Shutdown()
    {
        _cts.Cancel();
        // Wake any consumers blocked in WaitToReadAsync
        try { _wakeCts.Cancel(); } catch { }
        _logChannel.Writer.TryComplete();
        _spanChannel.Writer.TryComplete();
        _metricChannel.Writer.TryComplete();

        _logConsumer.Join(TimeSpan.FromSeconds(10));
        _spanConsumer.Join(TimeSpan.FromSeconds(10));
        _metricConsumer.Join(TimeSpan.FromSeconds(10));

        foreach (var d in _destinations)
            d.Shutdown();
    }

    public void Dispose() => Shutdown();

    /// <summary>Pipeline health: 0=healthy, 1=degraded, 2=down.</summary>
    public int GetStatus()
    {
        var snapshot = Metrics.GetSnapshot();
        if (snapshot.ExportErrors == 0) return 0;
        long totalExported = snapshot.LogsExported + snapshot.SpansExported + snapshot.MetricsExported;
        return totalExported > 0 ? 1 : 2;
    }

    // ── Consumer loop ──

    private void ConsumeLoop<T>(ChannelReader<T> reader, int maxBatchSize, int intervalMs,
        ManualResetEventSlim flushedEvent, Action<ReadOnlySpan<T>> writeBatch)
    {
        var batch = new List<T>(maxBatchSize);
        var token = _cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                batch.Clear();

                // Wait for at least one item or timeout
                try
                {
                    // Use wake token so Flush() can interrupt this wait
                    var wake = _wakeCts.Token;
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, wake);
                    if (reader.WaitToReadAsync(linked.Token).AsTask().Wait(intervalMs, linked.Token))
                    {
                        while (batch.Count < maxBatchSize && reader.TryRead(out var item))
                            batch.Add(item);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { /* wake for flush */ }
                catch (OperationCanceledException) { break; }
                catch (AggregateException ex) when (ex.InnerException is OperationCanceledException && !token.IsCancellationRequested) { /* wake for flush */ }
                catch (AggregateException ex) when (ex.InnerException is OperationCanceledException) { break; }

                if (batch.Count > 0)
                {
                    try
                    {
                        writeBatch(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(batch));
                    }
                    catch
                    {
                        Metrics.ExportError();
                    }
                }

                // Check flush flag: drain remaining and signal
                if (Volatile.Read(ref _flushRequested) == 1)
                {
                    batch.Clear();
                    while (reader.TryRead(out var extra))
                        batch.Add(extra);
                    if (batch.Count > 0)
                    {
                        try { writeBatch(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(batch)); }
                        catch { Metrics.ExportError(); }
                    }
                    flushedEvent.Set();
                    // Spin-wait until flush is acknowledged (reset by Flush caller)
                    while (Volatile.Read(ref _flushRequested) == 1 && !token.IsCancellationRequested)
                        Thread.Sleep(1);
                }
            }

            // Drain remaining items after cancellation/completion
            batch.Clear();
            while (reader.TryRead(out var remaining))
                batch.Add(remaining);

            if (batch.Count > 0)
            {
                try
                {
                    writeBatch(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(batch));
                }
                catch
                {
                    Metrics.ExportError();
                }
            }
        }
        finally
        {
            flushedEvent.Set();
        }
    }

    // ── Helpers ──

    public static long GetTimestampNano()
    {
        // DateTime.UtcNow in Unix nanoseconds
        return (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) * 1_000_000;
    }

    private static byte[] GenerateId(int length)
    {
        var id = new byte[length];
        Random.Shared.NextBytes(id);
        return id;
    }

    private sealed class ActiveSpan
    {
        public required byte[] TraceId { get; init; }
        public required byte[] SpanId { get; init; }
        public byte[]? ParentSpanId { get; init; }
        public required string Name { get; init; }
        public long StartTimeUnixNano { get; init; }
    }
}
