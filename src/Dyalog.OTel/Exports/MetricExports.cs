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
    /// pp_otel_metric pipeline metricName value metricType templateName attrs
    ///
    /// metricType: 0=Counter, 1=Gauge, 2=Histogram
    /// </summary>
    [DwaExport("pp_otel_metric")]
    public static void Metric(int pipeline, Localp metricName, Localp value, int metricType, Localp templateName, Localp attrs)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null) return;

        long timestamp = Pipeline.Pipeline.GetTimestampNano();
        string name = metricName.HasValue ? metricName.ReadString() : "unnamed";
        // ReadAsDoubles() handles APL type conversion (int→double etc.)
        // Bound() > 0 guard avoids IndexOutOfRange on empty arrays
        double val = 0.0;
        if (value.HasValue && value.Bound() > 0)
            val = value.ReadAsDoubles()[0];
        string tplName = templateName.HasValue ? templateName.ReadString() : "";

        TemplateSnapshot? snapshot = null;
        if (!string.IsNullOrEmpty(tplName))
            snapshot = pipe.Templates.TryGet(tplName);

        OTelAttribute[]? attributes = null;
        if (attrs.HasValue && attrs.IsNested())
            attributes = ExportHelpers.ReadAttributes(attrs);

        var point = new MetricPoint
        {
            TimestampUnixNano = timestamp,
            Name = name,
            Value = val,
            Type = (MetricType)metricType,
            Template = snapshot,
            Attributes = attributes
        };

        pipe.TryEnqueueMetric(point);
    }
}
