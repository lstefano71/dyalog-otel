using Dyalog.OTel.Templates;

namespace Dyalog.OTel.Channels;

/// <summary>
/// A span record captured on the hot path when a span ends.
/// </summary>
public sealed class SpanRecord
{
    /// <summary>Trace ID (16 bytes).</summary>
    public required byte[] TraceId { get; init; }

    /// <summary>Span ID (8 bytes).</summary>
    public required byte[] SpanId { get; init; }

    /// <summary>Parent span ID (8 bytes), or null for root spans.</summary>
    public byte[]? ParentSpanId { get; init; }

    /// <summary>Span name (operation).</summary>
    public required string Name { get; init; }

    /// <summary>Start time in Unix nanoseconds.</summary>
    public long StartTimeUnixNano { get; init; }

    /// <summary>End time in Unix nanoseconds.</summary>
    public long EndTimeUnixNano { get; init; }

    /// <summary>Span status: 0 = Unset, 1 = Ok, 2 = Error.</summary>
    public int StatusCode { get; init; }

    /// <summary>Pre-built template attributes (snapshot ref, GC-safe).</summary>
    public TemplateSnapshot? Template { get; init; }

    /// <summary>Inline attributes captured from the APL call.</summary>
    public OTelAttribute[]? Attributes { get; init; }

    /// <summary>Emitter name (InstrumentationScope). Null/empty = pipeline default.</summary>
    public string? Emitter { get; init; }
}