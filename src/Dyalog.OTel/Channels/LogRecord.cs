using Dyalog.OTel.Templates;

namespace Dyalog.OTel.Channels;

/// <summary>
/// A log record captured on the hot path, ready for background serialization.
/// </summary>
public sealed class LogRecord
{
    /// <summary>Timestamp captured on the interpreter thread (UTC).</summary>
    public long TimestampUnixNano { get; init; }

    /// <summary>OTel severity number (1-24).</summary>
    public int SeverityNumber { get; init; }

    /// <summary>Human-readable severity text.</summary>
    public string SeverityText { get; init; } = "";

    /// <summary>
    /// The raw message body. If this is a message template, it contains
    /// {Placeholder} tokens; the rendered form is produced at serialization time.
    /// </summary>
    public string Body { get; init; } = "";

    /// <summary>Trace ID (16 bytes) if correlated with a span, otherwise null.</summary>
    public byte[]? TraceId { get; init; }

    /// <summary>Span ID (8 bytes) if correlated with a span, otherwise null.</summary>
    public byte[]? SpanId { get; init; }

    /// <summary>Pre-built template attributes (snapshot ref, GC-safe).</summary>
    public TemplateSnapshot? Template { get; init; }

    /// <summary>Inline attributes captured from the APL call.</summary>
    public OTelAttribute[]? Attributes { get; init; }
}
