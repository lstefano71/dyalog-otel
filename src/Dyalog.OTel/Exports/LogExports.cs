using Dyalog.DWA;
using Dyalog.OTel.Channels;
using Dyalog.OTel.MessageTemplates;
using Dyalog.OTel.Pipeline;
using Dyalog.OTel.Templates;

namespace Dyalog.OTel.Exports;

/// <summary>
/// DWA exports for log signals. Hot path: read args → build struct → enqueue → return.
/// </summary>
public static class LogExports
{
    /// <summary>
    /// pp_otel_log pipeline severity message templateName attrs
    ///
    /// If message contains {Placeholders}, attrs are treated as positional fillers.
    /// If message is plain text, attrs are key-value pairs.
    /// templateName can be empty string for no template.
    /// </summary>
    [DwaExport("pp_otel_log")]
    public static void Log(int pipeline, int severity, Localp msg, Localp templateName, Localp attrs)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null) return;

        long timestamp = Pipeline.Pipeline.GetTimestampNano();
        string body = msg.HasValue ? msg.ReadString() : "";
        string tplName = templateName.HasValue ? templateName.ReadString() : "";

        // Resolve template snapshot
        TemplateSnapshot? snapshot = null;
        if (!string.IsNullOrEmpty(tplName))
            snapshot = pipe.Templates.TryGet(tplName);

        // Parse message template and extract attributes from fillers
        OTelAttribute[]? attributes = null;
        var parsed = TemplateCache.Instance.GetOrParse(body);
        if (parsed.HasPlaceholders && attrs.HasValue && attrs.Bound() > 0)
        {
            // Positional fillers mode: attrs contains values, template has names
            attributes = ExportHelpers.ReadFillers(attrs, parsed.PlaceholderNames);
        }
        else if (attrs.HasValue && attrs.Bound() > 0 && attrs.IsNested())
        {
            // Key-value pairs mode
            attributes = ExportHelpers.ReadAttributes(attrs);
        }

        var record = new LogRecord
        {
            TimestampUnixNano = timestamp,
            SeverityNumber = severity,
            SeverityText = SeverityToText(severity),
            Body = parsed.HasPlaceholders && attributes != null
                ? parsed.Render(attributes)
                : body,
            Template = snapshot,
            Attributes = attributes
        };

        pipe.TryEnqueueLog(record);
    }

    /// <summary>
    /// pp_otel_log_span pipeline severity message spanHandle templateName attrs
    ///
    /// Like pp_otel_log but correlates with an active span (log↔span correlation).
    /// </summary>
    [DwaExport("pp_otel_log_span")]
    public static void LogWithSpan(int pipeline, int severity, Localp msg, int spanHandle, Localp templateName, Localp attrs)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null) return;

        long timestamp = Pipeline.Pipeline.GetTimestampNano();
        string body = msg.HasValue ? msg.ReadString() : "";
        string tplName = templateName.HasValue ? templateName.ReadString() : "";

        TemplateSnapshot? snapshot = null;
        if (!string.IsNullOrEmpty(tplName))
            snapshot = pipe.Templates.TryGet(tplName);

        OTelAttribute[]? attributes = null;
        var parsed = TemplateCache.Instance.GetOrParse(body);
        if (parsed.HasPlaceholders && attrs.HasValue && attrs.Bound() > 0)
            attributes = ExportHelpers.ReadFillers(attrs, parsed.PlaceholderNames);
        else if (attrs.HasValue && attrs.Bound() > 0 && attrs.IsNested())
            attributes = ExportHelpers.ReadAttributes(attrs);

        // Resolve span context for correlation
        byte[]? traceId = null, spanId = null;
        if (spanHandle != 0)
        {
            var ctx = pipe.GetSpanContext(spanHandle);
            if (ctx.HasValue)
            {
                traceId = ctx.Value.TraceId;
                spanId = ctx.Value.SpanId;
            }
        }

        var record = new LogRecord
        {
            TimestampUnixNano = timestamp,
            SeverityNumber = severity,
            SeverityText = SeverityToText(severity),
            Body = parsed.HasPlaceholders && attributes != null
                ? parsed.Render(attributes)
                : body,
            TraceId = traceId,
            SpanId = spanId,
            Template = snapshot,
            Attributes = attributes
        };

        pipe.TryEnqueueLog(record);
    }

    private static string SeverityToText(int severity) => severity switch
    {
        <= 4 => "TRACE",
        <= 8 => "DEBUG",
        <= 12 => "INFO",
        <= 16 => "WARN",
        <= 20 => "ERROR",
        _ => "FATAL"
    };
}
