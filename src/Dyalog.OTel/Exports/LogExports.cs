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
    /// pp_otel_log pipeline severity message emitter templateName attrs
    ///
    /// If message is a nested vector, element 0 is the template with {Placeholders}
    /// and elements 1..N are positional filler values. attrs is then key-value pairs.
    /// If message is a flat string, it is the literal body. attrs is key-value pairs.
    /// emitter: scope name (empty string = pipeline default).
    /// templateName can be empty string for no template.
    /// </summary>
    [DwaExport("pp_otel_log")]
    public static void Log(int pipeline, int severity, Localp msg, Localp emitter, Localp templateName, Localp attrs)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null) return;

        long timestamp = Pipeline.Pipeline.GetTimestampNano();
        string emitterName = ExportHelpers.ReadOptionalString(emitter);
        string tplName = ExportHelpers.ReadOptionalString(templateName);

        // Resolve template snapshot
        TemplateSnapshot? snapshot = null;
        if (!string.IsNullOrEmpty(tplName))
            snapshot = pipe.Templates.TryGet(tplName);

        OTelAttribute[]? fillerAttributes = null;
        OTelAttribute[]? kvAttributes = null;
        MessageTemplate? parsed = null;
        string body;

        if (msg.IsNested())
        {
            // Bundled template mode: element 0 = template string, elements 1..N = fillers
            body = msg.ReadString(0);
            parsed = TemplateCache.Instance.GetOrParse(body);
            if (parsed.HasPlaceholders && msg.Bound() > 1)
                fillerAttributes = ExportHelpers.ReadFillersFromMessage(msg, parsed.PlaceholderNames);
        }
        else
        {
            body = msg.HasValue ? msg.ReadString() : "";
        }

        // attrs is always key-value pairs
        if (attrs.HasValue && attrs.Bound() >= 2 && attrs.IsNested())
            kvAttributes = ExportHelpers.ReadAttributes(attrs);

        // Merge filler attributes and key-value attributes (fillers take precedence)
        OTelAttribute[]? attributes = MergeAttributes(fillerAttributes, kvAttributes);

        var record = new LogRecord
        {
            TimestampUnixNano = timestamp,
            SeverityNumber = severity,
            SeverityText = SeverityToText(severity),
            Body = parsed?.HasPlaceholders == true && fillerAttributes != null
                ? parsed.Render(fillerAttributes)
                : body,
            Emitter = emitterName,
            Template = snapshot,
            Attributes = attributes
        };

        pipe.TryEnqueueLog(record);
    }

    /// <summary>
    /// pp_otel_log_span pipeline severity message spanHandle emitter templateName attrs
    ///
    /// Like pp_otel_log but correlates with an active span (log↔span correlation).
    /// Same bundled template rules as pp_otel_log.
    /// </summary>
    [DwaExport("pp_otel_log_span")]
    public static void LogWithSpan(int pipeline, int severity, Localp msg, int spanHandle, Localp emitter, Localp templateName, Localp attrs)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null) return;

        long timestamp = Pipeline.Pipeline.GetTimestampNano();
        string emitterName = ExportHelpers.ReadOptionalString(emitter);
        string tplName = ExportHelpers.ReadOptionalString(templateName);

        TemplateSnapshot? snapshot = null;
        if (!string.IsNullOrEmpty(tplName))
            snapshot = pipe.Templates.TryGet(tplName);

        OTelAttribute[]? fillerAttributes = null;
        OTelAttribute[]? kvAttributes = null;
        MessageTemplate? parsed = null;
        string body;

        if (msg.IsNested())
        {
            body = msg.ReadString(0);
            parsed = TemplateCache.Instance.GetOrParse(body);
            if (parsed.HasPlaceholders && msg.Bound() > 1)
                fillerAttributes = ExportHelpers.ReadFillersFromMessage(msg, parsed.PlaceholderNames);
        }
        else
        {
            body = msg.HasValue ? msg.ReadString() : "";
        }

        if (attrs.HasValue && attrs.Bound() >= 2 && attrs.IsNested())
            kvAttributes = ExportHelpers.ReadAttributes(attrs);

        OTelAttribute[]? attributes = MergeAttributes(fillerAttributes, kvAttributes);

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
            Body = parsed?.HasPlaceholders == true && fillerAttributes != null
                ? parsed.Render(fillerAttributes)
                : body,
            TraceId = traceId,
            SpanId = spanId,
            Emitter = emitterName,
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

    /// <summary>
    /// Merge filler-derived attributes with key-value attributes.
    /// Filler attributes take precedence on name collision.
    /// </summary>
    private static OTelAttribute[]? MergeAttributes(OTelAttribute[]? fillers, OTelAttribute[]? kvAttrs)
    {
        if (fillers == null && kvAttrs == null) return null;
        if (fillers == null) return kvAttrs;
        if (kvAttrs == null) return fillers;

        // Fillers take precedence: filter out kv attrs whose key matches a filler name
        var fillerKeys = new HashSet<string>(fillers.Select(a => a.Key), StringComparer.Ordinal);
        var extra = kvAttrs.Where(a => !fillerKeys.Contains(a.Key)).ToArray();
        if (extra.Length == 0) return fillers;

        var merged = new OTelAttribute[fillers.Length + extra.Length];
        fillers.CopyTo(merged, 0);
        extra.CopyTo(merged, fillers.Length);
        return merged;
    }
}
