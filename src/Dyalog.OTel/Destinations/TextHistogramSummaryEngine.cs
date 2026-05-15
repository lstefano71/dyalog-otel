using HdrHistogram;
using Dyalog.OTel.Channels;

namespace Dyalog.OTel.Destinations;

internal sealed class TextHistogramSummaryEngine
{
    private static readonly double[] DefaultBounds = { 0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000 };

    private readonly Dictionary<string, TextSummaryMetricRule> _rulesByMetric;
    private readonly Dictionary<SummaryKey, WindowState> _states = new();
    private readonly List<TextSummaryBlock> _ready = new();

    public TextHistogramSummaryEngine(IEnumerable<TextSummaryMetricRule> rules)
    {
        _rulesByMetric = rules.ToDictionary(rule => rule.Name, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasRules => _rulesByMetric.Count > 0;

    public void Observe(MetricPoint point, string emitter)
    {
        if (!_rulesByMetric.TryGetValue(point.Name, out var rule))
            return;

        var pointTime = FromUnixNano(point.TimestampUnixNano);
        var windowStart = AlignDown(pointTime, rule.Interval);
        var key = new SummaryKey(point.Name, emitter);

        if (_states.TryGetValue(key, out var state))
        {
            if (state.WindowStartUtc != windowStart)
            {
                if (state.Count > 0)
                    _ready.Add(state.Snapshot(windowStart, isPartial: false));

                state = new WindowState(rule, point.Name, emitter, windowStart);
                _states[key] = state;
            }
        }
        else
        {
            state = new WindowState(rule, point.Name, emitter, windowStart);
            _states[key] = state;
        }

        state.Record(point.Value);
    }

    public List<TextSummaryBlock> DrainReady(DateTimeOffset nowUtc)
    {
        var closed = new List<SummaryKey>();
        foreach (var (key, state) in _states)
        {
            DateTimeOffset alignedNow = AlignDown(nowUtc, state.Rule.Interval);
            if (state.Count > 0 && state.WindowStartUtc < alignedNow)
            {
                _ready.Add(state.Snapshot(state.WindowStartUtc + state.Rule.Interval, isPartial: false));
                closed.Add(key);
            }
        }

        foreach (var key in closed)
            _states.Remove(key);

        var result = new List<TextSummaryBlock>(_ready);
        _ready.Clear();
        return result;
    }

    public List<TextSummaryBlock> DrainPartial(DateTimeOffset nowUtc)
    {
        var result = new List<TextSummaryBlock>(_ready);
        _ready.Clear();

        foreach (var (key, state) in _states)
        {
            if (state.Count == 0)
                continue;

            result.Add(state.Snapshot(nowUtc, isPartial: true));
        }

        _states.Clear();
        return result;
    }

    private static DateTimeOffset AlignDown(DateTimeOffset timestampUtc, TimeSpan interval)
    {
        long ticks = timestampUtc.UtcDateTime.Ticks;
        long intervalTicks = interval.Ticks;
        long alignedTicks = ticks - (ticks % intervalTicks);
        return new DateTimeOffset(alignedTicks, TimeSpan.Zero);
    }

    private static DateTimeOffset FromUnixNano(long unixNano)
    {
        long ticks = unixNano / 100;
        return new DateTimeOffset(DateTime.UnixEpoch.Ticks + ticks, TimeSpan.Zero);
    }

    private readonly record struct SummaryKey(string MetricName, string Emitter);

    private sealed class WindowState
    {
        private readonly LongHistogram _histogram = new(3_600_000_000L, 3);
        private readonly long[] _bucketCounts = new long[DefaultBounds.Length + 1];

        public WindowState(TextSummaryMetricRule rule, string metricName, string emitter, DateTimeOffset windowStartUtc)
        {
            Rule = rule;
            MetricName = metricName;
            Emitter = emitter;
            WindowStartUtc = windowStartUtc;
        }

        public TextSummaryMetricRule Rule { get; }
        public string MetricName { get; }
        public string Emitter { get; }
        public DateTimeOffset WindowStartUtc { get; }
        public long Count { get; private set; }
        public double Sum { get; private set; }
        public double Min { get; private set; } = double.MaxValue;
        public double Max { get; private set; } = double.MinValue;

        public void Record(double value)
        {
            Count++;
            Sum += value;
            if (value < Min)
                Min = value;
            if (value > Max)
                Max = value;

            int bucketIndex = DefaultBounds.Length;
            for (int i = 0; i < DefaultBounds.Length; i++)
            {
                if (value <= DefaultBounds[i])
                {
                    bucketIndex = i;
                    break;
                }
            }

            _bucketCounts[bucketIndex]++;

            long roundedValue = Math.Max(1, (long)Math.Round(value));
            try
            {
                _histogram.RecordValue(roundedValue);
            }
            catch (IndexOutOfRangeException)
            {
            }
        }

        public TextSummaryBlock Snapshot(DateTimeOffset windowEndUtc, bool isPartial)
        {
            var percentiles = new List<TextPercentileValue>(Rule.Percentiles.Count);
            foreach (var percentile in Rule.Percentiles)
            {
                double value = Count > 0 ? _histogram.GetValueAtPercentile(percentile) : 0;
                percentiles.Add(new TextPercentileValue(percentile, value));
            }

            return new TextSummaryBlock
            {
                TimestampUtc = windowEndUtc,
                Emitter = Emitter,
                MetricName = MetricName,
                Interval = Rule.Interval,
                IsPartial = isPartial,
                Count = Count,
                Sum = Sum,
                Min = Count > 0 ? Min : 0,
                Max = Count > 0 ? Max : 0,
                IncludeBars = Rule.IncludeBars,
                Percentiles = percentiles,
                ExplicitBounds = (double[])DefaultBounds.Clone(),
                BucketCounts = (long[])_bucketCounts.Clone()
            };
        }
    }
}

internal sealed class TextSummaryBlock
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string Emitter { get; init; }
    public required string MetricName { get; init; }
    public required TimeSpan Interval { get; init; }
    public required bool IsPartial { get; init; }
    public required long Count { get; init; }
    public required double Sum { get; init; }
    public required double Min { get; init; }
    public required double Max { get; init; }
    public required bool IncludeBars { get; init; }
    public required IReadOnlyList<TextPercentileValue> Percentiles { get; init; }
    public required double[] ExplicitBounds { get; init; }
    public required long[] BucketCounts { get; init; }
}

internal sealed record TextPercentileValue(double Percentile, double Value);
