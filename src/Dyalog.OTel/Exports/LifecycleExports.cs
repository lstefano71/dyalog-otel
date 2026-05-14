using Dyalog.DWA;
using Dyalog.DWA.Native;
using Dyalog.OTel.Pipeline;

namespace Dyalog.OTel.Exports;

/// <summary>
/// DWA exports for pipeline lifecycle: init, start, shutdown, flush, config.
/// </summary>
public static class LifecycleExports
{
    /// <summary>
    /// pp_otel_init → 0 (singleton handle)
    /// Lazy-init: loads config file + env vars, starts consumer threads.
    /// </summary>
    [DwaExport("pp_otel_init")]
    public static void Init(Localp rslt)
    {
        PipelineRegistry.GetOrCreateSingleton();
        rslt.SetScalarInt(0);
    }

    /// <summary>
    /// pp_otel_shutdown pipeline
    /// Flush remaining data and tear down the pipeline.
    /// </summary>
    [DwaExport("pp_otel_shutdown")]
    public static void Shutdown(int pipeline)
    {
        PipelineRegistry.Shutdown(pipeline);
    }

    /// <summary>
    /// pp_otel_flush pipeline
    /// Block until all channels are drained and destinations flushed.
    /// </summary>
    [DwaExport("pp_otel_flush")]
    public static void Flush(int pipeline)
    {
        PipelineRegistry.Flush(pipeline);
    }

    /// <summary>
    /// pp_otel_status pipeline → 0/1/2 (healthy/degraded/down)
    /// </summary>
    [DwaExport("pp_otel_status")]
    public static void Status(int pipeline, Localp rslt)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        rslt.SetScalarInt(pipe?.GetStatus() ?? 2);
    }

    /// <summary>
    /// pp_otel_stats pipeline → 3×3 matrix of counters
    /// Rows: log, span, metric
    /// Cols: enqueued, exported, dropped
    /// </summary>
    [DwaExport("pp_otel_stats")]
    public static void Stats(int pipeline, Localp rslt)
    {
        var pipe = PipelineRegistry.Resolve(pipeline);
        if (pipe == null)
        {
            rslt.SetScalarInt(0);
            return;
        }

        var s = pipe.Metrics.GetSnapshot();
        // Return as a flat 9-element double vector (3×3 reshape in APL)
        var span = rslt.AllocVector<double>(ELTYPES.APLDOUB, 9);
        span[0] = s.LogsEnqueued;
        span[1] = s.LogsExported;
        span[2] = s.LogsDropped;
        span[3] = s.SpansEnqueued;
        span[4] = s.SpansExported;
        span[5] = s.SpansDropped;
        span[6] = s.MetricsEnqueued;
        span[7] = s.MetricsExported;
        span[8] = s.MetricsDropped;
    }

    /// <summary>
    /// pp_otel_noop — does nothing; measures bare DWA call overhead.
    /// </summary>
    [DwaExport("pp_otel_noop")]
    public static void Noop()
    {
    }

    /// <summary>
    /// pp_otel_add_dest pipeline type params
    /// Builder-phase: add a destination before start.
    /// </summary>
    [DwaExport("pp_otel_add_dest")]
    public static void AddDestination(int pipeline, Localp type, Localp paramsArg)
    {
        Console.Error.WriteLine("[dyalog-otel] WARNING: pp_otel_add_dest is not yet implemented. Configure destinations via INI file.");
    }

    /// <summary>
    /// pp_otel_resource pipeline attrs
    /// Builder-phase: set resource attributes.
    /// </summary>
    [DwaExport("pp_otel_resource")]
    public static void SetResource(int pipeline, Localp attrs)
    {
        Console.Error.WriteLine("[dyalog-otel] WARNING: pp_otel_resource is not yet implemented. Configure resource via INI file or OTEL_RESOURCE_ATTRIBUTES env var.");
    }
}
