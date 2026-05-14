namespace Dyalog.OTel.Channels;

/// <summary>
/// A captured key-value attribute pair. Values are stored as object
/// to support string, long, double, bool, and arrays thereof.
/// </summary>
public readonly record struct OTelAttribute(string Key, object Value);

/// <summary>
/// Severity levels aligned with OpenTelemetry severity numbers.
/// </summary>
public enum Severity
{
    Trace = 1,
    Debug = 5,
    Info = 9,
    Warn = 13,
    Error = 17,
    Fatal = 21
}

/// <summary>
/// Metric instrument type.
/// </summary>
public enum MetricType
{
    Counter = 0,
    Gauge = 1,
    Histogram = 2
}
