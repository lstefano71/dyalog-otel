using Dyalog.DWA;
using Dyalog.OTel.Pipeline;
using Dyalog.OTel.Templates;

namespace Dyalog.OTel.Exports;

/// <summary>
/// DWA exports for template management.
/// </summary>
public static class TemplateExports
{
    /// <summary>
    /// pp_otel_tpl_create pipeline name attrs
    /// </summary>
    [DwaExport("pp_otel_tpl_create")]
    public static void Create(int pipeline, Localp name, Localp attrs)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null) return;

        string tplName = name.ReadString();
        var attributes = ExportHelpers.ReadAttributesAsKvp(attrs);
        pipe.Templates.Create(tplName, attributes);
    }

    /// <summary>
    /// pp_otel_tpl_derive pipeline childName parentName extraAttrs
    /// </summary>
    [DwaExport("pp_otel_tpl_derive")]
    public static void Derive(int pipeline, Localp childName, Localp parentName, Localp extraAttrs)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null) return;

        string child = childName.ReadString();
        string parent = parentName.ReadString();
        var extras = ExportHelpers.ReadAttributesAsKvp(extraAttrs);
        pipe.Templates.Derive(child, parent, extras);
    }

    /// <summary>
    /// pp_otel_tpl_delete pipeline name
    /// </summary>
    [DwaExport("pp_otel_tpl_delete")]
    public static void Delete(int pipeline, Localp name)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null) return;

        pipe.Templates.Delete(name.ReadString());
    }
}
