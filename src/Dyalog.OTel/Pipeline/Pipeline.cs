using System.Diagnostics;
using System.Runtime.InteropServices;
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
/// batch consumers, and a set of destinations.
/// </summary>
public sealed class Pipeline : IDisposable
{
    private readonly Channel<LogRecord> _logChannel;
    private readonly Channel<SpanRecord> _spanChannel;
    private readonly Channel<MetricPoint> _metricChannel;

    private readonly Channel<List<LogRecord>> _logBatches;
    private readonly Channel<List<SpanRecord>> _spanBatches;
    private readonly Channel<List<MetricPoint>> _metricBatches;

    private Task? _logConsumer;
    private Task? _spanConsumer;
    private Task? _metricConsumer;

    private const int ConsumerWakeIntervalMs = 50;
    private const int FlushTimeoutMs = 10_000;
    private long _flushEpoch;

    private readonly int _logBatchSize;
    private readonly int _spanBatchSize;
    private readonly int _metricBatchSize;
    private readonly int _logBatchIntervalMs;
    private readonly int _spanBatchIntervalMs;
    private readonly int _metricBatchIntervalMs;

    // Watermark counters: items attempted (success or failure), for flush synchronization
    private long _logProcessed;
    private long _spanProcessed;
    private long _metricProcessed;

    private readonly List<IDestination> _destinations;
    public InternalMetrics Metrics { get; } = new();
    public TemplateRegistry Templates { get; } = new();
    public Dictionary<string, string> Resource { get; set; } = new();

    // Interpreter-thread-only: DWA guarantees all exports are called from a single thread
    private readonly Dictionary<int, ActiveSpan> _activeSpans = new();
    private int _nextSpanHandle = 1;

    public Pipeline(List<IDestination> destinations, BatchConfig batchConfig, int channelCapacity = 8192)
    {
        _destinations = destinations;

        var opts = new BoundedChannelOptions(channelCapacity)
        {
            // TryWrite must return false on a full channel so drops are counted accurately.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true   // APL interpreter is single-threaded
        };

        _logChannel = Channel.CreateBounded<LogRecord>(opts);
        _spanChannel = Channel.CreateBounded<SpanRecord>(opts);
        _metricChannel = Channel.CreateBounded<MetricPoint>(opts);
        _logBatches = CreateBatchChannel<LogRecord>();
        _spanBatches = CreateBatchChannel<SpanRecord>();
        _metricBatches = CreateBatchChannel<MetricPoint>();

        _logBatchSize = ValidateBatchSize(batchConfig.LogSize, nameof(batchConfig.LogSize));
        _spanBatchSize = ValidateBatchSize(batchConfig.SpanSize, nameof(batchConfig.SpanSize));
        _metricBatchSize = ValidateBatchSize(batchConfig.MetricSize, nameof(batchConfig.MetricSize));
        _logBatchIntervalMs = batchConfig.LogIntervalMs;
        _spanBatchIntervalMs = batchConfig.SpanIntervalMs;
        _metricBatchIntervalMs = batchConfig.MetricIntervalMs;
    }

    public void Start()
    {
        foreach (var d in _destinations)
            d.Init();

        var logBatcher = Task.Run(() => BatchChannel(
            _logChannel.Reader,
            _logBatches.Writer,
            _logBatchSize,
            _logBatchIntervalMs));
        var logExporter = Task.Run(() => ExportBatches(_logBatches.Reader,
            (List<LogRecord> batch) =>
            {
                try
                {
                    foreach (var d in _destinations)
                        d.WriteLogs(CollectionsMarshal.AsSpan(batch));
                    Metrics.LogExported(batch.Count);
                }
                catch (Exception ex) { Metrics.ExportError(); Console.Error.WriteLine($"[LOG EXPORT ERR] {ex}"); }
                finally { Interlocked.Add(ref _logProcessed, batch.Count); }
            }));
        _logConsumer = Task.WhenAll(logBatcher, logExporter);

        var spanBatcher = Task.Run(() => BatchChannel(
            _spanChannel.Reader,
            _spanBatches.Writer,
            _spanBatchSize,
            _spanBatchIntervalMs));
        var spanExporter = Task.Run(() => ExportBatches(_spanBatches.Reader,
            (List<SpanRecord> batch) =>
            {
                try
                {
                    foreach (var d in _destinations)
                        d.WriteSpans(CollectionsMarshal.AsSpan(batch));
                    Metrics.SpanExported(batch.Count);
                }
                catch (Exception ex) { Metrics.ExportError(); Console.Error.WriteLine($"[SPAN EXPORT ERR] {ex}"); }
                finally { Interlocked.Add(ref _spanProcessed, batch.Count); }
            }));
        _spanConsumer = Task.WhenAll(spanBatcher, spanExporter);

        var metricBatcher = Task.Run(() => BatchChannel(
            _metricChannel.Reader,
            _metricBatches.Writer,
            _metricBatchSize,
            _metricBatchIntervalMs));
        var metricExporter = Task.Run(() => ExportBatches(_metricBatches.Reader,
            (List<MetricPoint> batch) =>
            {
                try { WriteMetricBatch(CollectionsMarshal.AsSpan(batch)); }
                catch (Exception ex) { Metrics.ExportError(); Console.Error.WriteLine($"[METRIC EXPORT ERR] {ex}"); }
                finally { Interlocked.Add(ref _metricProcessed, batch.Count); }
            }));
        _metricConsumer = Task.WhenAll(metricBatcher, metricExporter);
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
        ActiveSpan? parent = null;
        if (parentHandle != 0)
            _activeSpans.TryGetValue(parentHandle, out parent);
        byte[] resolvedTraceId = traceId ?? parent?.TraceId ?? GenerateId(16);
        byte[]? parentSpanId = parent?.SpanId;

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

    // ── Flush / Shutdown ──

    public void Flush()
    {
        // Capture watermarks (APL is single-threaded, no new enqueues after this)
        var snap = Metrics.GetSnapshot();
        long logTarget = snap.LogsEnqueued;
        long spanTarget = snap.SpansEnqueued;
        long metricTarget = snap.MetricsEnqueued;

        // Wake consumers so they emit any partial batch up to this watermark.
        Interlocked.Increment(ref _flushEpoch);

        // Wait until consumers have processed everything up to the watermark
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < FlushTimeoutMs)
        {
            long lp = Interlocked.Read(ref _logProcessed);
            long sp = Interlocked.Read(ref _spanProcessed);
            long mp = Interlocked.Read(ref _metricProcessed);

            if (lp >= logTarget && sp >= spanTarget && mp >= metricTarget)
                break;

            if (_logConsumer?.IsFaulted == true
                || _spanConsumer?.IsFaulted == true
                || _metricConsumer?.IsFaulted == true)
                break;

            Thread.Sleep(1);
        }

        FlushHistograms();

        foreach (var d in _destinations)
            d.Flush();
    }

    private static Channel<List<T>> CreateBatchChannel<T>() =>
        Channel.CreateUnbounded<List<T>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

    private async Task BatchChannel<T>(
        ChannelReader<T> reader,
        ChannelWriter<List<T>> writer,
        int batchSize,
        int batchIntervalMs)
    {
        var batch = new List<T>(batchSize);
        long batchStartedAt = 0;
        long seenFlushEpoch = Interlocked.Read(ref _flushEpoch);

        try
        {
            while (true)
            {
                while (batch.Count < batchSize && reader.TryRead(out var item))
                {
                    if (batch.Count == 0)
                        batchStartedAt = Stopwatch.GetTimestamp();
                    batch.Add(item);
                }

                long currentFlushEpoch = Interlocked.Read(ref _flushEpoch);
                bool flushRequested = currentFlushEpoch != seenFlushEpoch;
                bool intervalElapsed = batch.Count > 0
                    && batchIntervalMs > 0
                    && Stopwatch.GetElapsedTime(batchStartedAt).TotalMilliseconds >= batchIntervalMs;
                bool completed = reader.Completion.IsCompleted;

                if (batch.Count >= batchSize || (batch.Count > 0 && (flushRequested || intervalElapsed || completed)))
                {
                    EmitBatch(writer, ref batch, batchSize);
                    batchStartedAt = 0;

                    if (flushRequested)
                        seenFlushEpoch = currentFlushEpoch;
                    continue;
                }

                if (flushRequested)
                {
                    seenFlushEpoch = currentFlushEpoch;
                    continue;
                }

                if (completed)
                {
                    writer.TryComplete();
                    return;
                }

                int waitMs = GetConsumerWaitMs(batchIntervalMs, batchStartedAt, batch.Count);
                using var wake = new CancellationTokenSource(waitMs);
                try
                {
                    if (!await reader.WaitToReadAsync(wake.Token).ConfigureAwait(false))
                    {
                        EmitBatch(writer, ref batch, batchSize);
                        writer.TryComplete();
                        return;
                    }
                }
                catch (OperationCanceledException) when (wake.IsCancellationRequested)
                {
                }
            }
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
            throw;
        }
    }

    private static void EmitBatch<T>(ChannelWriter<List<T>> writer, ref List<T> batch, int batchSize)
    {
        if (batch.Count == 0)
            return;
        if (!writer.TryWrite(batch))
            throw new InvalidOperationException("Batch channel rejected a telemetry batch.");
        batch = new List<T>(batchSize);
    }

    private static async Task ExportBatches<T>(
        ChannelReader<List<T>> reader,
        Action<List<T>> consume)
    {
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var batch))
                consume(batch);
        }
    }

    private static int ValidateBatchSize(int batchSize, string name)
    {
        if (batchSize < 1)
            throw new ArgumentOutOfRangeException(name, batchSize, "Batch size must be at least 1.");
        return batchSize;
    }

    private static int GetConsumerWaitMs(int batchIntervalMs, long batchStartedAt, int batchCount)
    {
        int waitMs = ConsumerWakeIntervalMs;
        if (batchCount > 0 && batchIntervalMs > 0)
        {
            long remaining = batchIntervalMs - (long)Stopwatch.GetElapsedTime(batchStartedAt).TotalMilliseconds;
            if (remaining <= 0)
                return 1;
            if (remaining < waitMs)
                waitMs = (int)remaining;
        }
        return waitMs;
    }

    public void Shutdown()
    {
        // Completing the writers lets consumers drain any remaining items and return.
        _logChannel.Writer.TryComplete();
        _spanChannel.Writer.TryComplete();
        _metricChannel.Writer.TryComplete();

        if (_logConsumer != null && _spanConsumer != null && _metricConsumer != null)
        {
            try
            {
                Task.WaitAll([_logConsumer, _spanConsumer, _metricConsumer],
                    TimeSpan.FromSeconds(10));
            }
            catch (AggregateException) { /* consumer may have faulted — destinations still need cleanup */ }
        }

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

    // Windows FILETIME epoch (Jan 1, 1601) to Unix epoch (Jan 1, 1970) in 100ns ticks
    private const long FileTimeToUnixEpochTicks = 116_444_736_000_000_000L;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern void GetSystemTimePreciseAsFileTime(out long fileTime);

    /// <summary>
    /// High-resolution UTC timestamp in Unix nanoseconds (~100ns precision).
    /// Uses GetSystemTimePreciseAsFileTime instead of DateTime.UtcNow (15.6ms resolution).
    /// </summary>
    public static long GetTimestampNano()
    {
        GetSystemTimePreciseAsFileTime(out long fileTime);
        return (fileTime - FileTimeToUnixEpochTicks) * 100;
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
        var rawHistograms = new List<MetricPoint>();
        foreach (var point in batch)
        {
            if (point.Type == Channels.MetricType.Histogram)
            {
                _histogramAggregator.Record(point.Name, point.Value);
                rawHistograms.Add(point);
            }
            else
                nonHistogram.Add(point);
        }

        // Write non-histogram metrics directly
        if (nonHistogram.Count > 0)
        {
            foreach (var d in _destinations)
                d.WriteMetrics(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(nonHistogram));
        }

        if (rawHistograms.Count > 0)
        {
            var rawSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rawHistograms);
            foreach (var d in _destinations)
            {
                if (d is IRawHistogramObservationDestination rawHistogramDestination)
                    rawHistogramDestination.WriteRawHistogramMetrics(rawSpan);
            }
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
            {
                if (d is not IRawHistogramObservationDestination)
                    d.WriteMetrics(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(points));
            }
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
