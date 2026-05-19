using System.Text.Json.Serialization;

namespace Dyalog.OTel.Serialization.JsonModels;

// ── Common ──

public sealed class OtlpKeyValue
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public OtlpAnyValue Value { get; set; } = new();
}

public sealed class OtlpAnyValue
{
    [JsonPropertyName("stringValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StringValue { get; set; }

    [JsonPropertyName("intValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntValue { get; set; } // OTLP encodes ints as strings

    [JsonPropertyName("doubleValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DoubleValue { get; set; }

    [JsonPropertyName("boolValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? BoolValue { get; set; }
}

public sealed class OtlpResource
{
    [JsonPropertyName("attributes")]
    public List<OtlpKeyValue> Attributes { get; set; } = new();
}

public sealed class OtlpInstrumentationScope
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}

// ── Logs ──

public sealed class OtlpExportLogsRequest
{
    [JsonPropertyName("resourceLogs")]
    public List<OtlpResourceLogs> ResourceLogs { get; set; } = new();
}

public sealed class OtlpResourceLogs
{
    [JsonPropertyName("resource")]
    public OtlpResource Resource { get; set; } = new();

    [JsonPropertyName("scopeLogs")]
    public List<OtlpScopeLogs> ScopeLogs { get; set; } = new();
}

public sealed class OtlpScopeLogs
{
    [JsonPropertyName("scope")]
    public OtlpInstrumentationScope Scope { get; set; } = new();

    [JsonPropertyName("logRecords")]
    public List<OtlpLogRecord> LogRecords { get; set; } = new();
}

public sealed class OtlpLogRecord
{
    [JsonPropertyName("timeUnixNano")]
    public string TimeUnixNano { get; set; } = "0"; // OTLP uses string for uint64

    [JsonPropertyName("severityNumber")]
    public int SeverityNumber { get; set; }

    [JsonPropertyName("severityText")]
    public string? SeverityText { get; set; }

    [JsonPropertyName("body")]
    public OtlpAnyValue? Body { get; set; }

    [JsonPropertyName("attributes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OtlpKeyValue>? Attributes { get; set; }

    [JsonPropertyName("traceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; set; } // hex-encoded

    [JsonPropertyName("spanId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpanId { get; set; } // hex-encoded
}

// ── Traces ──

public sealed class OtlpExportTraceRequest
{
    [JsonPropertyName("resourceSpans")]
    public List<OtlpResourceSpans> ResourceSpans { get; set; } = new();
}

public sealed class OtlpResourceSpans
{
    [JsonPropertyName("resource")]
    public OtlpResource Resource { get; set; } = new();

    [JsonPropertyName("scopeSpans")]
    public List<OtlpScopeSpans> ScopeSpans { get; set; } = new();
}

public sealed class OtlpScopeSpans
{
    [JsonPropertyName("scope")]
    public OtlpInstrumentationScope Scope { get; set; } = new();

    [JsonPropertyName("spans")]
    public List<OtlpSpan> Spans { get; set; } = new();
}

public sealed class OtlpSpan
{
    [JsonPropertyName("traceId")]
    public string TraceId { get; set; } = "";

    [JsonPropertyName("spanId")]
    public string SpanId { get; set; } = "";

    [JsonPropertyName("parentSpanId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentSpanId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("kind")]
    public int Kind { get; set; } = 1; // SPAN_KIND_INTERNAL

    [JsonPropertyName("startTimeUnixNano")]
    public string StartTimeUnixNano { get; set; } = "0";

    [JsonPropertyName("endTimeUnixNano")]
    public string EndTimeUnixNano { get; set; } = "0";

    [JsonPropertyName("status")]
    public OtlpStatus? Status { get; set; }

    [JsonPropertyName("attributes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OtlpKeyValue>? Attributes { get; set; }
}

public sealed class OtlpStatus
{
    [JsonPropertyName("code")]
    public int Code { get; set; } // 0=Unset, 1=Ok, 2=Error
}

// ── Metrics ──

public sealed class OtlpExportMetricsRequest
{
    [JsonPropertyName("resourceMetrics")]
    public List<OtlpResourceMetrics> ResourceMetrics { get; set; } = new();
}

public sealed class OtlpResourceMetrics
{
    [JsonPropertyName("resource")]
    public OtlpResource Resource { get; set; } = new();

    [JsonPropertyName("scopeMetrics")]
    public List<OtlpScopeMetrics> ScopeMetrics { get; set; } = new();
}

public sealed class OtlpScopeMetrics
{
    [JsonPropertyName("scope")]
    public OtlpInstrumentationScope Scope { get; set; } = new();

    [JsonPropertyName("metrics")]
    public List<OtlpMetric> Metrics { get; set; } = new();
}

public sealed class OtlpMetric
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("sum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OtlpSum? Sum { get; set; }

    [JsonPropertyName("gauge")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OtlpGauge? Gauge { get; set; }

    [JsonPropertyName("histogram")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OtlpHistogram? Histogram { get; set; }
}

public sealed class OtlpSum
{
    [JsonPropertyName("dataPoints")]
    public List<OtlpNumberDataPoint> DataPoints { get; set; } = new();

    [JsonPropertyName("isMonotonic")]
    public bool IsMonotonic { get; set; } = true;

    [JsonPropertyName("aggregationTemporality")]
    public int AggregationTemporality { get; set; } = 2; // DELTA
}

public sealed class OtlpGauge
{
    [JsonPropertyName("dataPoints")]
    public List<OtlpNumberDataPoint> DataPoints { get; set; } = new();
}

public sealed class OtlpHistogram
{
    [JsonPropertyName("dataPoints")]
    public List<OtlpHistogramDataPoint> DataPoints { get; set; } = new();

    [JsonPropertyName("aggregationTemporality")]
    public int AggregationTemporality { get; set; } = 2; // DELTA
}

public sealed class OtlpNumberDataPoint
{
    [JsonPropertyName("timeUnixNano")]
    public string TimeUnixNano { get; set; } = "0";

    [JsonPropertyName("asDouble")]
    public double AsDouble { get; set; }

    [JsonPropertyName("attributes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OtlpKeyValue>? Attributes { get; set; }
}

public sealed class OtlpHistogramDataPoint
{
    [JsonPropertyName("timeUnixNano")]
    public string TimeUnixNano { get; set; } = "0";

    [JsonPropertyName("count")]
    public string Count { get; set; } = "0";

    [JsonPropertyName("sum")]
    public double Sum { get; set; }

    [JsonPropertyName("min")]
    public double Min { get; set; }

    [JsonPropertyName("max")]
    public double Max { get; set; }

    [JsonPropertyName("bucketCounts")]
    public List<string> BucketCounts { get; set; } = new();

    [JsonPropertyName("explicitBounds")]
    public List<double> ExplicitBounds { get; set; } = new();

    [JsonPropertyName("attributes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OtlpKeyValue>? Attributes { get; set; }
}
