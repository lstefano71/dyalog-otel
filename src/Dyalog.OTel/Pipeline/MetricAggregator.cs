using Dyalog.OTel.Channels;
using Dyalog.OTel.Templates;
using HdrHistogram;

namespace Dyalog.OTel.Pipeline;

/// <summary>
/// Aggregates all metric types on the consumer thread before export.
/// Counter → sum deltas per series. Gauge → last-value-wins. Histogram → buckets/percentiles.
/// Series key: metric name + canonicalized merged attributes (template + inline, sorted by key).
///
/// FUTURE EXTENSION: Raw gauge observations for destinations that need spike detection.
/// Same pattern as IRawHistogramObservationDestination — an opt-in interface (e.g.
/// IRawGaugeObservationDestination) that receives every gauge observation before
/// last-value-wins aggregation. Not built until a destination actually needs it.
/// </summary>
internal sealed class MetricAggregator
{
    private static readonly double[] DefaultHistogramBounds = { 0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000 };

    private readonly Dictionary<SeriesKey, CounterState> _counters = new();
    private readonly Dictionary<SeriesKey, GaugeState> _gauges = new();
    private readonly Dictionary<SeriesKey, HistogramState> _histograms = new();
    private long _intervalStartNano;

    public MetricAggregator()
    {
        _intervalStartNano = Pipeline.GetTimestampNano();
    }

    /// <summary>Record an incoming metric observation.</summary>
    public void Record(MetricPoint point)
    {
        var key = BuildKey(point);

        switch (point.Type)
        {
            case MetricType.Counter:
                if (!_counters.TryGetValue(key, out var counter))
                {
                    counter = new CounterState();
                    _counters[key] = counter;
                }
                counter.Add(point.Value);
                break;

            case MetricType.Gauge:
                if (!_gauges.TryGetValue(key, out var gauge))
                {
                    gauge = new GaugeState();
                    _gauges[key] = gauge;
                }
                gauge.Set(point.Value, point.TimestampUnixNano);
                break;

            case MetricType.Histogram:
                if (!_histograms.TryGetValue(key, out var histogram))
                {
                    histogram = new HistogramState();
                    _histograms[key] = histogram;
                }
                histogram.Record(point.Value, DefaultHistogramBounds);
                break;
        }
    }

    /// <summary>
    /// Snapshot all accumulated state and reset for the next interval.
    /// Returns aggregated MetricPoints ready for destination export.
    /// </summary>
    public List<MetricPoint> SnapshotAndReset()
    {
        long endNano = Pipeline.GetTimestampNano();
        long startNano = _intervalStartNano;
        _intervalStartNano = endNano;

        var result = new List<MetricPoint>(_counters.Count + _gauges.Count + _histograms.Count);

        // Counters: sum of deltas
        foreach (var (key, state) in _counters)
        {
            if (state.Sum == 0 && state.Count == 0) continue;
            result.Add(new MetricPoint
            {
                Name = key.Name,
                Type = MetricType.Counter,
                Value = state.Sum,
                TimestampUnixNano = endNano,
                StartTimeUnixNano = startNano,
                Attributes = key.Attributes,
                Emitter = key.Emitter,
            });
            state.Reset();
        }

        // Gauges: last value
        foreach (var (key, state) in _gauges)
        {
            if (!state.HasValue) continue;
            result.Add(new MetricPoint
            {
                Name = key.Name,
                Type = MetricType.Gauge,
                Value = state.Value,
                TimestampUnixNano = state.TimestampNano,
                StartTimeUnixNano = startNano,
                Attributes = key.Attributes,
                Emitter = key.Emitter,
            });
            state.Reset();
        }

        // Histograms: bucket/percentile snapshot
        foreach (var (key, state) in _histograms)
        {
            var snap = state.Snapshot(DefaultHistogramBounds);
            if (snap.Count == 0) continue;
            result.Add(new MetricPoint
            {
                Name = key.Name,
                Type = MetricType.Histogram,
                Value = snap.Sum,
                TimestampUnixNano = endNano,
                StartTimeUnixNano = startNano,
                Emitter = key.Emitter,
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
            state.Reset();
        }

        return result;
    }

    /// <summary>Whether there are any recorded observations pending snapshot.</summary>
    public bool HasData => _counters.Count > 0 || _gauges.Count > 0 || _histograms.Count > 0;

    #region Series Key

    private static SeriesKey BuildKey(MetricPoint point)
    {
        var attrs = CanonicalizeMergedAttributes(point.Template, point.Attributes);
        return new SeriesKey(point.Name, point.Emitter ?? "", attrs);
    }

    /// <summary>
    /// Merge template + inline attributes, sort by key, deduplicate (inline wins).
    /// Returns null if no attributes.
    /// </summary>
    internal static OTelAttribute[]? CanonicalizeMergedAttributes(TemplateSnapshot? template, OTelAttribute[]? inline)
    {
        int templateCount = template?.Attributes.Count ?? 0;
        int inlineCount = inline?.Length ?? 0;

        if (templateCount == 0 && inlineCount == 0)
            return null;

        // Merge into ordered dictionary (inline overrides template for same key)
        var merged = new SortedDictionary<string, object>(StringComparer.Ordinal);
        if (template != null)
        {
            foreach (var kv in template.Attributes)
                merged[kv.Key] = kv.Value;
        }
        if (inline != null)
        {
            foreach (var kv in inline)
                merged[kv.Key] = kv.Value;
        }

        var result = new OTelAttribute[merged.Count];
        int i = 0;
        foreach (var kv in merged)
            result[i++] = new OTelAttribute(kv.Key, kv.Value);
        return result;
    }

    #endregion

    #region State classes

    private sealed class CounterState
    {
        public double Sum { get; private set; }
        public int Count { get; private set; }
        public void Add(double value) { Sum += value; Count++; }
        public void Reset() { Sum = 0; Count = 0; }
    }

    private sealed class GaugeState
    {
        public double Value { get; private set; }
        public long TimestampNano { get; private set; }
        public bool HasValue { get; private set; }
        public void Set(double value, long timestampNano) { Value = value; TimestampNano = timestampNano; HasValue = true; }
        public void Reset() { HasValue = false; }
    }

    private sealed class HistogramState
    {
        private LongHistogram _hdr = new(3_600_000_000L, 3);
        private long[] _buckets = null!;
        private double[] _bounds = null!;
        private double _sum;
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private long _count;

        public void EnsureBounds(double[] bounds)
        {
            if (_bounds == null || !ReferenceEquals(_bounds, bounds))
            {
                _bounds = bounds;
                _buckets = new long[bounds.Length + 1];
            }
        }

        public void Record(double value, double[] bounds)
        {
            EnsureBounds(bounds);
            _count++;
            _sum += value;
            if (value < _min) _min = value;
            if (value > _max) _max = value;

            int bucket = _bounds.Length;
            for (int i = 0; i < _bounds.Length; i++)
            {
                if (value <= _bounds[i]) { bucket = i; break; }
            }
            _buckets[bucket]++;

            long longVal = Math.Max(1, (long)Math.Round(value));
            try { _hdr.RecordValue(longVal); }
            catch (IndexOutOfRangeException) { /* value outside range */ }
        }

        public HistogramSnapshot Snapshot(double[] bounds)
        {
            EnsureBounds(bounds);
            return new HistogramSnapshot
            {
                Count = _count,
                Sum = _sum,
                Min = _count > 0 ? _min : 0,
                Max = _count > 0 ? _max : 0,
                ExplicitBounds = bounds,
                BucketCounts = (long[])_buckets.Clone(),
                P50 = _count > 0 ? _hdr.GetValueAtPercentile(50) : 0,
                P90 = _count > 0 ? _hdr.GetValueAtPercentile(90) : 0,
                P99 = _count > 0 ? _hdr.GetValueAtPercentile(99) : 0,
            };
        }

        public void Reset()
        {
            _hdr.Reset();
            if (_buckets != null) Array.Clear(_buckets, 0, _buckets.Length);
            _sum = 0;
            _min = double.MaxValue;
            _max = double.MinValue;
            _count = 0;
        }
    }

    #endregion
}

/// <summary>
/// Identifies a unique metric series: name + emitter + canonical attribute set.
/// </summary>
internal readonly struct SeriesKey : IEquatable<SeriesKey>
{
    public string Name { get; }
    public string Emitter { get; }
    public OTelAttribute[]? Attributes { get; }
    private readonly int _hash;

    public SeriesKey(string name, string emitter, OTelAttribute[]? attributes)
    {
        Name = name;
        Emitter = emitter;
        Attributes = attributes;
        _hash = ComputeHash(name, emitter, attributes);
    }

    private static int ComputeHash(string name, string emitter, OTelAttribute[]? attrs)
    {
        var hash = new HashCode();
        hash.Add(name, StringComparer.Ordinal);
        hash.Add(emitter, StringComparer.Ordinal);
        if (attrs != null)
        {
            foreach (var attr in attrs)
            {
                hash.Add(attr.Key, StringComparer.Ordinal);
                hash.Add(attr.Value?.GetHashCode() ?? 0);
            }
        }
        return hash.ToHashCode();
    }

    public bool Equals(SeriesKey other)
    {
        if (_hash != other._hash) return false;
        if (!string.Equals(Name, other.Name, StringComparison.Ordinal)) return false;
        if (!string.Equals(Emitter, other.Emitter, StringComparison.Ordinal)) return false;
        return AttributesEqual(Attributes, other.Attributes);
    }

    private static bool AttributesEqual(OTelAttribute[]? a, OTelAttribute[]? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i].Key, b[i].Key, StringComparison.Ordinal)) return false;
            if (!Equals(a[i].Value, b[i].Value)) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is SeriesKey other && Equals(other);
    public override int GetHashCode() => _hash;
}

/// <summary>
/// Aggregated histogram data for a single metric name.
/// </summary>
internal sealed class HistogramSnapshot
{
    public long Count { get; init; }
    public double Sum { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double[] ExplicitBounds { get; init; } = Array.Empty<double>();
    public long[] BucketCounts { get; init; } = Array.Empty<long>();
    public long P50 { get; init; }
    public long P90 { get; init; }
    public long P99 { get; init; }
}
