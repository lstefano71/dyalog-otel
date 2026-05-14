using System.Security.Cryptography;
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

    // Interpreter-thread-only: DWA guarantees all exports are called from a single thread
    private readonly Dictionary<int, ActiveSpan> _activeSpans = new();
    private int _nextSpanHandle = 1;

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
            (batch) => { WriteMetricBatch(batch); }))
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

    public int StartSpan(string name, int parentHandle, byte[]? traceId, OTelAttribute[]? startAttrs = null, Templates.TemplateSnapshot? startTemplate = null)
    {
        int handle = _nextSpanHandle++;
        if (handle <= 0) // wrapped past int.MaxValue or hit 0
        {
            _nextSpanHandle = 2;
            handle = 1;
        }
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
            StartTimeUnixNano = GetTimestampNano(),
            StartAttributes = startAttrs,
            StartTemplate = startTemplate
        };
        return handle;
    }

    // Interpreter-thread-only: no lock needed (single-thread DWA guarantee)
    public (byte[] TraceId, byte[] SpanId)? GetSpanContext(int spanHandle)
    {
        if (_activeSpans.TryGetValue(spanHandle, out var span))
            return (span.TraceId, span.SpanId);
        return null;
    }

    public bool EndSpan(int spanHandle, OTelAttribute[]? attrs, Templates.TemplateSnapshot? template)
    {
        if (!_activeSpans.Remove(spanHandle, out var span))
            return false;

        // Merge start-time and end-time attributes (end overrides start for same keys)
        OTelAttribute[]? mergedAttrs = MergeAttributes(span.StartAttributes, attrs);
        Templates.TemplateSnapshot? mergedTemplate = template ?? span.StartTemplate;

        var record = new SpanRecord
        {
            TraceId = span.TraceId,
            SpanId = span.SpanId,
            ParentSpanId = span.ParentSpanId,
            Name = span.Name,
            StartTimeUnixNano = span.StartTimeUnixNano,
            EndTimeUnixNano = GetTimestampNano(),
            Template = mergedTemplate,
            Attributes = mergedAttrs
        };

        if (_spanChannel.Writer.TryWrite(record))
        {
            Metrics.SpanEnqueued();
            return true;
        }
        Metrics.SpanDropped();
        return false;
    }

    private volatile TaskCompletionSource<bool>? _flushTcs;

    // ── Flush / Shutdown ──

    public void Flush()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _logFlushed.Reset();
        _spanFlushed.Reset();
        _metricFlushed.Reset();

        // Publish the TCS so consumers see a flush request
        _flushTcs = tcs;

        // Keep waking consumers until all have acknowledged the flush.
        // This handles the race where a consumer grabs a new wake token
        // between our exchange-and-cancel.
        var deadline = TimeSpan.FromSeconds(30);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < deadline)
        {
            var old = Interlocked.Exchange(ref _wakeCts, new CancellationTokenSource());
            old.Cancel();
            old.Dispose();

            bool allSet = _logFlushed.Wait(TimeSpan.FromMilliseconds(100))
                       && _spanFlushed.Wait(TimeSpan.FromMilliseconds(100))
                       && _metricFlushed.Wait(TimeSpan.FromMilliseconds(100));
            if (allSet) break;
        }

        // Clear the TCS so consumers resume normal operation
        _flushTcs = null;

        // Flush aggregated histograms
        FlushHistograms();

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

                // Check for pending flush before entering a potentially blocking wait
                if (_flushTcs != null)
                {
                    DrainAndSignalFlush(reader, batch, writeBatch, flushedEvent, token);
                    continue;
                }

                // Wait for at least one item or timeout
                try
                {
                    // Pass wake token to WaitToReadAsync (flush interrupts it),
                    // and shutdown token to Wait() — avoids per-iteration linked CTS allocation
                    var wake = _wakeCts.Token;
                    var waitTask = reader.WaitToReadAsync(wake).AsTask();
                    if (waitTask.Wait(intervalMs, token))
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

                // Check flush TCS after batch processing
                if (_flushTcs != null)
                {
                    DrainAndSignalFlush(reader, batch, writeBatch, flushedEvent, token);
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

    private void DrainAndSignalFlush<T>(ChannelReader<T> reader, List<T> batch,
        Action<ReadOnlySpan<T>> writeBatch, ManualResetEventSlim flushedEvent, CancellationToken token)
    {
        var myTcs = _flushTcs; // capture the specific TCS we're handling
        batch.Clear();
        while (reader.TryRead(out var extra))
            batch.Add(extra);
        if (batch.Count > 0)
        {
            try { writeBatch(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(batch)); }
            catch { Metrics.ExportError(); }
        }
        flushedEvent.Set();
        // Wait for THIS specific flush to complete — break if TCS reference changes
        // (covers the case where Flush() clears it to null, or a new Flush() replaces it)
        while (ReferenceEquals(_flushTcs, myTcs) && !token.IsCancellationRequested)
            Thread.Sleep(1);
    }

    public static long GetTimestampNano()
    {
        // DateTime.UtcNow in Unix nanoseconds
        return (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) * 1_000_000;
    }

    private static byte[] GenerateId(int length)
    {
        var id = new byte[length];
        RandomNumberGenerator.Fill(id);
        return id;
    }

    private static OTelAttribute[]? MergeAttributes(OTelAttribute[]? startAttrs, OTelAttribute[]? endAttrs)
    {
        if (startAttrs == null) return endAttrs;
        if (endAttrs == null) return startAttrs;

        // End attrs override start attrs for same keys
        var merged = new Dictionary<string, object>();
        foreach (var a in startAttrs)
            merged[a.Key] = a.Value;
        foreach (var a in endAttrs)
            merged[a.Key] = a.Value;

        return merged.Select(kv => new OTelAttribute(kv.Key, kv.Value)).ToArray();
    }

    private readonly HistogramAggregator _histogramAggregator = new();

    private void WriteMetricBatch(ReadOnlySpan<MetricPoint> batch)
    {
        // Separate histogram observations from other metrics
        var nonHistogram = new List<MetricPoint>();
        foreach (var point in batch)
        {
            if (point.Type == Channels.MetricType.Histogram)
                _histogramAggregator.Record(point.Name, point.Value);
            else
                nonHistogram.Add(point);
        }

        // Write non-histogram metrics directly
        if (nonHistogram.Count > 0)
        {
            foreach (var d in _destinations)
                d.WriteMetrics(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(nonHistogram));
        }

        Metrics.MetricExported(batch.Length);
    }

    private void FlushHistograms()
    {
        var snapshots = _histogramAggregator.SnapshotAndReset();
        if (snapshots.Count == 0) return;

        var points = new List<MetricPoint>();
        foreach (var (name, snap) in snapshots)
        {
            if (snap.Count == 0) continue;
            points.Add(new MetricPoint
            {
                TimestampUnixNano = GetTimestampNano(),
                Name = name,
                Value = snap.Sum,
                Type = Channels.MetricType.Histogram,
                Attributes = new[]
                {
                    new OTelAttribute("histogram.count", snap.Count),
                    new OTelAttribute("histogram.sum", snap.Sum),
                    new OTelAttribute("histogram.min", snap.Min),
                    new OTelAttribute("histogram.max", snap.Max),
                    new OTelAttribute("histogram.p50", snap.P50),
                    new OTelAttribute("histogram.p90", snap.P90),
                    new OTelAttribute("histogram.p99", snap.P99),
                }
            });
        }

        if (points.Count > 0)
        {
            foreach (var d in _destinations)
                d.WriteMetrics(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(points));
        }
    }

    private sealed class ActiveSpan
    {
        public required byte[] TraceId { get; init; }
        public required byte[] SpanId { get; init; }
        public byte[]? ParentSpanId { get; init; }
        public required string Name { get; init; }
        public long StartTimeUnixNano { get; init; }
        public OTelAttribute[]? StartAttributes { get; init; }
        public Templates.TemplateSnapshot? StartTemplate { get; init; }
    }
}
