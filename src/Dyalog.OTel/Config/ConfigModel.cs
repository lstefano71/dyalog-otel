namespace Dyalog.OTel.Config;

/// <summary>
/// Typed configuration model parsed from INI sections.
/// </summary>
public sealed class OTelConfig
{
    public const int DefaultFlushTimeoutMs = 10_000;

    /// <summary>Resource attributes (service.name, environment, etc.).</summary>
    public Dictionary<string, string> Resource { get; set; } = new();

    /// <summary>Configured destinations.</summary>
    public List<DestinationConfig> Destinations { get; set; } = new();

    /// <summary>Batch configuration per signal type.</summary>
    public BatchConfig Batch { get; set; } = new();

    /// <summary>Maximum time pp_otel_flush / graceful shutdown waits for consumers to drain.</summary>
    public int FlushTimeoutMs { get; set; } = DefaultFlushTimeoutMs;

    /// <summary>Default emitter name (InstrumentationScope.name). Fallback: "dyalog-otel".</summary>
    public string DefaultEmitter { get; set; } = "dyalog-otel";

    /// <summary>Default emitter version (InstrumentationScope.version). Fallback: library assembly version.</summary>
    public string DefaultEmitterVersion { get; set; } = "";

    /// <summary>Registered emitter name → version mappings for lazily-loaded components.</summary>
    public Dictionary<string, string> EmitterRegistry { get; set; } = new(StringComparer.Ordinal);
}

public sealed class DestinationConfig
{
    /// <summary>Destination type: file, http, otlp.</summary>
    public required string Type { get; set; }

    /// <summary>All key-value properties from the INI section.</summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>Which signal types this destination accepts.</summary>
    public HashSet<string> Signals { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "log", "span", "metric" };
}

public sealed class BatchConfig
{
    public int LogSize { get; set; } = 512;
    public int LogIntervalMs { get; set; } = 5000;
    public int SpanSize { get; set; } = 256;
    public int SpanIntervalMs { get; set; } = 2000;
    public int MetricSize { get; set; } = 256;
    public int MetricIntervalMs { get; set; } = 60000;
}
