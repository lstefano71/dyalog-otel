using LightProto;

namespace Dyalog.OTel.Serialization.ProtoModels;

// ── Common ──

[ProtoContract]
public partial class ProtoResource
{
    [ProtoMember(1)]
    public List<ProtoKeyValue> Attributes { get; set; } = new();

    [ProtoMember(2)]
    public uint DroppedAttributesCount { get; set; }
}

[ProtoContract]
public partial class ProtoInstrumentationScope
{
    [ProtoMember(1)]
    public string Name { get; set; } = "";

    [ProtoMember(2)]
    public string Version { get; set; } = "";

    [ProtoMember(3)]
    public List<ProtoKeyValue> Attributes { get; set; } = new();
}

[ProtoContract]
public partial class ProtoKeyValue
{
    [ProtoMember(1)]
    public string Key { get; set; } = "";

    [ProtoMember(2)]
    public ProtoAnyValue? Value { get; set; }
}

[ProtoContract]
public partial class ProtoAnyValue
{
    [ProtoMember(1)]
    public string? StringValue { get; set; }

    [ProtoMember(2)]
    public bool BoolValue { get; set; }

    [ProtoMember(3)]
    public long IntValue { get; set; }

    [ProtoMember(4)]
    public double DoubleValue { get; set; }

    [ProtoMember(5)]
    public ProtoArrayValue? ArrayValue { get; set; }

    [ProtoMember(6)]
    public ProtoKeyValueList? KvlistValue { get; set; }

    [ProtoMember(7)]
    public byte[]? BytesValue { get; set; }
}

[ProtoContract]
public partial class ProtoArrayValue
{
    [ProtoMember(1)]
    public List<ProtoAnyValue> Values { get; set; } = new();
}

[ProtoContract]
public partial class ProtoKeyValueList
{
    [ProtoMember(1)]
    public List<ProtoKeyValue> Values { get; set; } = new();
}

// ── Logs ──

[ProtoContract]
public partial class ProtoExportLogsServiceRequest
{
    [ProtoMember(1)]
    public List<ProtoResourceLogs> ResourceLogs { get; set; } = new();
}

[ProtoContract]
public partial class ProtoResourceLogs
{
    [ProtoMember(1)]
    public ProtoResource? Resource { get; set; }

    [ProtoMember(2)]
    public List<ProtoScopeLogs> ScopeLogs { get; set; } = new();
}

[ProtoContract]
public partial class ProtoScopeLogs
{
    [ProtoMember(1)]
    public ProtoInstrumentationScope? Scope { get; set; }

    [ProtoMember(2)]
    public List<ProtoLogRecord> LogRecords { get; set; } = new();
}

[ProtoContract]
public partial class ProtoLogRecord
{
    [ProtoMember(1, DataFormat = DataFormat.FixedSize)]
    public ulong TimeUnixNano { get; set; }

    [ProtoMember(11, DataFormat = DataFormat.FixedSize)]
    public ulong ObservedTimeUnixNano { get; set; }

    [ProtoMember(2)]
    public ProtoSeverityNumber SeverityNumber { get; set; }

    [ProtoMember(3)]
    public string? SeverityText { get; set; }

    [ProtoMember(5)]
    public ProtoAnyValue? Body { get; set; }

    [ProtoMember(6)]
    public List<ProtoKeyValue> Attributes { get; set; } = new();

    [ProtoMember(9)]
    public byte[]? TraceId { get; set; }

    [ProtoMember(10)]
    public byte[]? SpanId { get; set; }
}

[ProtoContract]
public enum ProtoSeverityNumber
{
    Unspecified = 0,
    Trace = 1,
    Trace2 = 2,
    Trace3 = 3,
    Trace4 = 4,
    Debug = 5,
    Debug2 = 6,
    Debug3 = 7,
    Debug4 = 8,
    Info = 9,
    Info2 = 10,
    Info3 = 11,
    Info4 = 12,
    Warn = 13,
    Warn2 = 14,
    Warn3 = 15,
    Warn4 = 16,
    Error = 17,
    Error2 = 18,
    Error3 = 19,
    Error4 = 20,
    Fatal = 21,
    Fatal2 = 22,
    Fatal3 = 23,
    Fatal4 = 24
}

// ── Traces ──

[ProtoContract]
public partial class ProtoExportTraceServiceRequest
{
    [ProtoMember(1)]
    public List<ProtoResourceSpans> ResourceSpans { get; set; } = new();
}

[ProtoContract]
public partial class ProtoResourceSpans
{
    [ProtoMember(1)]
    public ProtoResource? Resource { get; set; }

    [ProtoMember(2)]
    public List<ProtoScopeSpans> ScopeSpans { get; set; } = new();
}

[ProtoContract]
public partial class ProtoScopeSpans
{
    [ProtoMember(1)]
    public ProtoInstrumentationScope? Scope { get; set; }

    [ProtoMember(2)]
    public List<ProtoSpan> Spans { get; set; } = new();
}

[ProtoContract]
public partial class ProtoSpan
{
    [ProtoMember(1)]
    public byte[]? TraceId { get; set; }

    [ProtoMember(2)]
    public byte[]? SpanId { get; set; }

    [ProtoMember(3)]
    public string? TraceState { get; set; }

    [ProtoMember(4)]
    public byte[]? ParentSpanId { get; set; }

    [ProtoMember(5)]
    public string Name { get; set; } = "";

    [ProtoMember(6)]
    public ProtoSpanKind Kind { get; set; }

    [ProtoMember(7, DataFormat = DataFormat.FixedSize)]
    public ulong StartTimeUnixNano { get; set; }

    [ProtoMember(8, DataFormat = DataFormat.FixedSize)]
    public ulong EndTimeUnixNano { get; set; }

    [ProtoMember(9)]
    public List<ProtoKeyValue> Attributes { get; set; } = new();

    [ProtoMember(15)]
    public ProtoStatus? Status { get; set; }
}

[ProtoContract]
public enum ProtoSpanKind
{
    Unspecified = 0,
    Internal = 1,
    Server = 2,
    Client = 3,
    Producer = 4,
    Consumer = 5
}

[ProtoContract]
public partial class ProtoStatus
{
    [ProtoMember(2)]
    public string? Message { get; set; }

    [ProtoMember(3)]
    public ProtoStatusCode Code { get; set; }
}

[ProtoContract]
public enum ProtoStatusCode
{
    Unset = 0,
    Ok = 1,
    Error = 2
}

// ── Metrics ──

[ProtoContract]
public partial class ProtoExportMetricsServiceRequest
{
    [ProtoMember(1)]
    public List<ProtoResourceMetrics> ResourceMetrics { get; set; } = new();
}

[ProtoContract]
public partial class ProtoResourceMetrics
{
    [ProtoMember(1)]
    public ProtoResource? Resource { get; set; }

    [ProtoMember(2)]
    public List<ProtoScopeMetrics> ScopeMetrics { get; set; } = new();
}

[ProtoContract]
public partial class ProtoScopeMetrics
{
    [ProtoMember(1)]
    public ProtoInstrumentationScope? Scope { get; set; }

    [ProtoMember(2)]
    public List<ProtoMetric> Metrics { get; set; } = new();
}

[ProtoContract]
public partial class ProtoMetric
{
    [ProtoMember(1)]
    public string Name { get; set; } = "";

    [ProtoMember(2)]
    public string? Description { get; set; }

    [ProtoMember(3)]
    public string? Unit { get; set; }

    [ProtoMember(5)]
    public ProtoGauge? Gauge { get; set; }

    [ProtoMember(7)]
    public ProtoSum? Sum { get; set; }

    [ProtoMember(9)]
    public ProtoHistogram? Histogram { get; set; }
}

[ProtoContract]
public partial class ProtoGauge
{
    [ProtoMember(1)]
    public List<ProtoNumberDataPoint> DataPoints { get; set; } = new();
}

[ProtoContract]
public partial class ProtoSum
{
    [ProtoMember(1)]
    public List<ProtoNumberDataPoint> DataPoints { get; set; } = new();

    [ProtoMember(2)]
    public ProtoAggregationTemporality AggregationTemporality { get; set; }

    [ProtoMember(3)]
    public bool IsMonotonic { get; set; }
}

[ProtoContract]
public partial class ProtoHistogram
{
    [ProtoMember(1)]
    public List<ProtoNumberDataPoint> DataPoints { get; set; } = new();

    [ProtoMember(2)]
    public ProtoAggregationTemporality AggregationTemporality { get; set; }
}

[ProtoContract]
public enum ProtoAggregationTemporality
{
    Unspecified = 0,
    Delta = 1,
    Cumulative = 2
}

[ProtoContract]
public partial class ProtoNumberDataPoint
{
    [ProtoMember(7)]
    public List<ProtoKeyValue> Attributes { get; set; } = new();

    [ProtoMember(2, DataFormat = DataFormat.FixedSize)]
    public ulong StartTimeUnixNano { get; set; }

    [ProtoMember(3, DataFormat = DataFormat.FixedSize)]
    public ulong TimeUnixNano { get; set; }

    [ProtoMember(4)]
    public double AsDouble { get; set; }

    [ProtoMember(6, DataFormat = DataFormat.FixedSize)]
    public long AsInt { get; set; }
}
