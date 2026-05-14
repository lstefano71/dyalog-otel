using HdrHistogram;

namespace Dyalog.OTel.Pipeline;

/// <summary>
/// Accumulates histogram observations per metric name.
/// Used on consumer threads (not hot path) to aggregate raw MetricPoint observations
/// into bucket/percentile data before export.
/// </summary>
internal sealed class HistogramAggregator
{
    // Default explicit bucket boundaries (aligned with OTel SDK defaults)
    private static readonly double[] DefaultBounds = { 0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000 };

    private readonly Dictionary<string, HistogramState> _histograms = new();

    public void Record(string metricName, double value)
    {
        if (!_histograms.TryGetValue(metricName, out var state))
        {
            state = new HistogramState();
            _histograms[metricName] = state;
        }
        state.Record(value, DefaultBounds);
    }

    /// <summary>
    /// Snapshot and reset all histograms. Returns aggregated data per metric name.
    /// </summary>
    public Dictionary<string, HistogramSnapshot> SnapshotAndReset()
    {
        var result = new Dictionary<string, HistogramSnapshot>();
        foreach (var (name, state) in _histograms)
        {
            result[name] = state.Snapshot(DefaultBounds);
            state.Reset();
        }
        return result;
    }

    private sealed class HistogramState
    {
        // HdrHistogram for percentile calculations (3 sig digits, 1 to 3.6B range)
        private LongHistogram _hdr = new(3_600_000_000L, 3);
        // Explicit bucket counts maintained separately (HdrHistogram API varies by version)
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
                _buckets = new long[bounds.Length + 1]; // +1 for overflow
            }
        }

        public void Record(double value, double[] bounds)
        {
            EnsureBounds(bounds);
            _count++;
            _sum += value;
            if (value < _min) _min = value;
            if (value > _max) _max = value;

            // Assign to explicit bucket
            int bucket = _bounds.Length; // overflow by default
            for (int i = 0; i < _bounds.Length; i++)
            {
                if (value <= _bounds[i]) { bucket = i; break; }
            }
            _buckets[bucket]++;

            // HdrHistogram for percentiles
            long longVal = Math.Max(1, (long)Math.Round(value));
            try { _hdr.RecordValue(longVal); }
            catch (IndexOutOfRangeException) { /* value outside range — tracked in sum/count */ }
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
            Array.Clear(_buckets, 0, _buckets.Length);
            _sum = 0;
            _min = double.MaxValue;
            _max = double.MinValue;
            _count = 0;
        }
    }
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
