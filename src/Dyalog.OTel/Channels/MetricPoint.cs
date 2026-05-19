using Dyalog.OTel.Templates;

namespace Dyalog.OTel.Channels;

/// <summary>
/// A metric data point captured on the hot path.
/// </summary>
public sealed class MetricPoint
{
    /// <summary>Timestamp captured on the interpreter thread (UTC).</summary>
    public long TimestampUnixNano { get; init; }

    /// <summary>Metric instrument name.</summary>
    public required string Name { get; init; }

    /// <summary>The observed value (delta for counters, absolute for gauges, observation for histograms).</summary>
    public double Value { get; init; }

    /// <summary>Instrument type: Counter, Gauge, or Histogram.</summary>
    public MetricType Type { get; init; }

    /// <summary>Pre-built template attributes (snapshot ref, GC-safe).</summary>
    public TemplateSnapshot? Template { get; init; }

    /// <summary>Inline attributes captured from the APL call.</summary>
    public OTelAttribute[]? Attributes { get; init; }

    /// <summary>Emitter name (InstrumentationScope). Null/empty = pipeline default.</summary>
    public string? Emitter { get; init; }
}
