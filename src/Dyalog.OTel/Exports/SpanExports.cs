using Dyalog.DWA;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Pipeline;
using Dyalog.OTel.Templates;

namespace Dyalog.OTel.Exports;

/// <summary>
/// DWA exports for span signals.
/// </summary>
public static class SpanExports
{
    /// <summary>
    /// pp_otel_span_start pipeline name parentHandle templateName attrs → spanHandle
    /// </summary>
    [DwaExport("pp_otel_span_start")]
    public static void SpanStart(int pipeline, Localp name, int parentHandle, Localp templateName, Localp attrs, Localp rslt)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null)
        {
            rslt.SetScalarInt(0);
            return;
        }

        string spanName = name.HasValue ? name.ReadString() : "unnamed";
        string tplName = templateName.HasValue ? templateName.ReadString() : "";

        // Read start-time template and attributes
        Templates.TemplateSnapshot? snapshot = null;
        if (!string.IsNullOrEmpty(tplName))
            snapshot = pipe.Templates.TryGet(tplName);

        OTelAttribute[]? attributes = null;
        if (attrs.HasValue && attrs.IsNested())
            attributes = ExportHelpers.ReadAttributes(attrs);

        // Start the span (allocates handle, generates trace/span IDs)
        int handle = pipe.StartSpan(spanName, parentHandle, null, attributes, snapshot);

        rslt.SetScalarInt(handle);
    }

    /// <summary>
    /// pp_otel_span_end pipeline spanHandle attrs
    /// </summary>
    [DwaExport("pp_otel_span_end")]
    public static void SpanEnd(int pipeline, int spanHandle, Localp templateName, Localp attrs)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null) return;

        string tplName = templateName.HasValue ? templateName.ReadString() : "";

        TemplateSnapshot? snapshot = null;
        if (!string.IsNullOrEmpty(tplName))
            snapshot = pipe.Templates.TryGet(tplName);

        OTelAttribute[]? attributes = null;
        if (attrs.HasValue && attrs.IsNested())
            attributes = ExportHelpers.ReadAttributes(attrs);

        pipe.EndSpan(spanHandle, attributes, snapshot);
    }
}
