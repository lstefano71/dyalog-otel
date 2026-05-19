using Dyalog.DWA;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Pipeline;
using Dyalog.OTel.Templates;

namespace Dyalog.OTel.Exports;

/// <summary>
/// DWA exports for metric signals.
/// </summary>
public static class MetricExports
{
    /// <summary>
    /// pp_otel_metric pipeline metricName value metricType emitter templateName attrs
    ///
    /// metricType: 0=Counter, 1=Gauge, 2=Histogram
    /// emitter: scope name (empty string = pipeline default)
    /// </summary>
    [DwaExport("pp_otel_metric")]
    public static void Metric(int pipeline, Localp metricName, Localp value, int metricType, Localp emitter, Localp templateName, Localp attrs)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null) return;

        long timestamp = Pipeline.Pipeline.GetTimestampNano();
        string name = metricName.HasValue ? metricName.ReadString() : "unnamed";
        double val = ExportHelpers.ReadFirstDouble(value);
        string emitterName = ExportHelpers.ReadOptionalString(emitter);
        string tplName = ExportHelpers.ReadOptionalString(templateName);

        TemplateSnapshot? snapshot = null;
        if (!string.IsNullOrEmpty(tplName))
            snapshot = pipe.Templates.TryGet(tplName);

        OTelAttribute[]? attributes = null;
        if (attrs.HasValue && attrs.Bound() > 0 && attrs.IsNested())
            attributes = ExportHelpers.ReadAttributes(attrs);

        var point = new MetricPoint
        {
            TimestampUnixNano = timestamp,
            Name = name,
            Value = val,
            Type = (MetricType)metricType,
            Emitter = emitterName,
            Template = snapshot,
            Attributes = attributes
        };

        pipe.TryEnqueueMetric(point);
    }
}
