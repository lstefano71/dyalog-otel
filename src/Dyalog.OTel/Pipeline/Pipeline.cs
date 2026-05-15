using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;
using Dyalog.OTel.Diagnostics;
using Dyalog.OTel.Templates;
using Open.ChannelExtensions;

namespace Dyalog.OTel.Pipeline;

/// <summary>
/// A telemetry pipeline: owns three typed channels (log/span/metric),
/// batching readers (via Open.ChannelExtensions), and a set of destinations.
/// </summary>
public sealed class Pipeline : IDisposable
{
    private readonly Channel<LogRecord> _logChannel;
    private readonly Channel<SpanRecord> _spanChannel;
    private readonly Channel<MetricPoint> _metricChannel;

    private readonly BatchingChannelReader<LogRecord, List<LogRecord>> _logBatcher;
    private readonly BatchingChannelReader<SpanRecord, List<SpanRecord>> _spanBatcher;
    private readonly BatchingChannelReader<MetricPoint, List<MetricPoint>> _metricBatcher;

    private Task? _logConsumer;
    private Task? _spanConsumer;
    private Task? _metricConsumer;

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
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = false, // ForceBatch reads from APL thread concurrently with consumer
            SingleWriter = true   // APL interpreter is single-threaded
        };

        _logChannel = Channel.CreateBounded<LogRecord>(opts);
        _spanChannel = Channel.CreateBounded<SpanRecord>(opts);
        _metricChannel = Channel.CreateBounded<MetricPoint>(opts);

        _logBatcher = _logChannel.Reader
            .Batch(batchConfig.LogSize, singleReader: true)
            .WithTimeout(batchConfig.LogIntervalMs);
        _spanBatcher = _spanChannel.Reader
            .Batch(batchConfig.SpanSize, singleReader: true)
            .WithTimeout(batchConfig.SpanIntervalMs);
        _metricBatcher = _metricChannel.Reader
            .Batch(batchConfig.MetricSize, singleReader: true)
            .WithTimeout(batchConfig.MetricIntervalMs);
    }

    public void Start()
    {
        foreach (var d in _destinations)
            d.Init();

        _logConsumer = Task.Run(async () => await _logBatcher.ReadAll(
            (List<LogRecord> batch) =>
            {
                try
                {
                    foreach (var d in _destinations)
                        d.WriteLogs(CollectionsMarshal.AsSpan(batch));
                    Metrics.LogExported(batch.Count);
                }
                catch { Metrics.ExportError(); }
                finally { Interlocked.Add(ref _logProcessed, batch.Count); }
            }));

        _spanConsumer = Task.Run(async () => await _spanBatcher.ReadAll(
            (List<SpanRecord> batch) =>
            {
                try
                {
                    foreach (var d in _destinations)
                        d.WriteSpans(CollectionsMarshal.AsSpan(batch));
                    Metrics.SpanExported(batch.Count);
                }
                catch { Metrics.ExportError(); }
                finally { Interlocked.Add(ref _spanProcessed, batch.Count); }
            }));

        _metricConsumer = Task.Run(async () => await _metricBatcher.ReadAll(
            (List<MetricPoint> batch) =>
            {
                try { WriteMetricBatch(CollectionsMarshal.AsSpan(batch)); }
                catch { Metrics.ExportError(); }
                finally { Interlocked.Add(ref _metricProcessed, batch.Count); }
            }));
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

    // ── Flush / Shutdown ──

    public void Flush()
    {
        // Capture watermarks (APL is single-threaded, no new enqueues after this)
        var snap = Metrics.GetSnapshot();
        long logTarget = snap.LogsEnqueued;
        long spanTarget = snap.SpansEnqueued;
        long metricTarget = snap.MetricsEnqueued;

        // Push any partial batches into the consumer pipeline
        _logBatcher.ForceBatch();
        _spanBatcher.ForceBatch();
        _metricBatcher.ForceBatch();

        // Wait until consumers have processed everything up to the watermark
        var sw = System.Diagnostics.Stopwatch.StartNew();
        SpinWait spinner = new();
        while (sw.ElapsedMilliseconds < 30_000)
        {
            if (Interlocked.Read(ref _logProcessed) >= logTarget
                && Interlocked.Read(ref _spanProcessed) >= spanTarget
                && Interlocked.Read(ref _metricProcessed) >= metricTarget)
                break;

            // Bail early if a consumer has faulted (counters will never advance)
            if (_logConsumer?.IsFaulted == true
                || _spanConsumer?.IsFaulted == true
                || _metricConsumer?.IsFaulted == true)
                break;

            spinner.SpinOnce();
        }

        FlushHistograms();

        foreach (var d in _destinations)
            d.Flush();
    }

    public void Shutdown()
    {
        // Completing the writers triggers the batchers' automatic final flush,
        // then ReadAll processes all remaining batches and returns.
        _logChannel.Writer.TryComplete();
        _spanChannel.Writer.TryComplete();
        _metricChannel.Writer.TryComplete();

        try
        {
            Task.WaitAll([_logConsumer!, _spanConsumer!, _metricConsumer!],
                TimeSpan.FromSeconds(10));
        }
        catch (AggregateException) { /* consumer may have faulted — destinations still need cleanup */ }

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
