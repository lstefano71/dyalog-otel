using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;
using Dyalog.OTel.Resources;

namespace Dyalog.OTel.Pipeline;

/// <summary>
/// Global registry of pipelines. Handle 0 is the lazy singleton.
/// Thread-safe for handle lookup (background threads read, interpreter thread writes).
/// </summary>
public static class PipelineRegistry
{
    private static volatile Pipeline? _singleton;
    private static readonly ConcurrentDictionary<int, Pipeline> _pipelines = new();
    private static int _nextHandle = 1;
    private static readonly object _initLock = new();

    private static volatile PipelineState _singletonState = PipelineState.Unconfigured;

    /// <summary>
    /// Static constructor: register a ProcessExit handler so that if the host process
    /// exits without calling pp_otel_shutdown (e.g. after an APL error), we still
    /// flush and shut down gracefully. Best-effort — won't fire on hard crashes.
    /// </summary>
    static PipelineRegistry()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ShutdownAll();
        RegisterNativeAtexit();
    }

    /// <summary>
    /// Pre-init config overrides (layer 4). Applied on top of INI + env at init time.
    /// Key = "section.key", value = string.
    /// </summary>
    private static readonly Dictionary<string, string> _preInitOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns 0 if not yet running, 1 if already running.</summary>
    public static int GetSingletonState() =>
        _singletonState == PipelineState.Running ? 1 : 0;

    /// <summary>
    /// Get or lazy-init the singleton pipeline (handle 0).
    /// </summary>
    public static Pipeline GetOrCreateSingleton()
    {
        if (_singleton != null && _singletonState == PipelineState.Running)
            return _singleton;

        lock (_initLock)
        {
            if (_singleton != null && _singletonState == PipelineState.Running)
                return _singleton;

            if (_singletonState == PipelineState.Unconfigured || _singletonState == PipelineState.Configuring)
            {
                // Lazy init: load config, apply layer-4 overrides, create pipeline, start
                var config = ConfigLoader.Load();
                ApplyOverridesToConfig(config);
                _singleton = BuildPipeline(config);
                _singleton.Start();
                _singletonState = PipelineState.Running;
            }
        }

        return _singleton!;
    }

    /// <summary>Begin configuring the singleton (explicit init mode).</summary>
    public static PipelineBuilder InitSingleton()
    {
        _singletonState = PipelineState.Configuring;
        return new PipelineBuilder();
    }

    /// <summary>
    /// Apply a config override (layer 4). Returns 0 on success.
    /// Pre-init: stores override. Post-init: only emitter.* sections allowed (returns 2 if frozen).
    /// </summary>
    public static int ApplyConfigOverride(string section, string key, string value)
    {
        bool isEmitterSection = section.StartsWith("emitter.", StringComparison.OrdinalIgnoreCase);

        if (_singletonState == PipelineState.Running)
        {
            // Post-init: only emitter registration is allowed
            if (!isEmitterSection)
                return 2; // Frozen section

            string emitterName = section["emitter.".Length..];
            if (key.Equals("version", StringComparison.OrdinalIgnoreCase) && _singleton != null)
            {
                _singleton.EmitterRegistry[emitterName] = value;
                return 0;
            }
            return 1; // Unknown key
        }

        // Pre-init: accumulate for merge at init time
        string fullKey = $"{section}.{key}";
        lock (_initLock)
        {
            _preInitOverrides[fullKey] = value;
        }
        return 0;
    }

    /// <summary>Freeze config and start the singleton.</summary>
    public static void StartSingleton(PipelineBuilder builder)
    {
        _singleton = builder.Build();
        _singletonState = PipelineState.Running;
        _singleton.Start();
    }

    /// <summary>Create an explicit pipeline with a new handle.</summary>
    public static int CreatePipeline(OTelConfig config)
    {
        var pipeline = BuildPipeline(config);
        int handle = Interlocked.Increment(ref _nextHandle);
        _pipelines[handle] = pipeline;
        pipeline.Start();
        return handle;
    }

    /// <summary>Resolve a pipeline by handle. 0 = singleton.</summary>
    public static Pipeline? Resolve(int handle)
    {
        if (handle == 0) return GetOrCreateSingleton();
        _pipelines.TryGetValue(handle, out var pipeline);
        return pipeline;
    }

    /// <summary>Shutdown and remove a pipeline.</summary>
    public static void Shutdown(int handle)
    {
        if (handle == 0)
        {
            _singleton?.Shutdown();
            _singleton = null;
            _singletonState = PipelineState.Unconfigured;
            lock (_initLock) { _preInitOverrides.Clear(); }
            return;
        }

        if (_pipelines.TryRemove(handle, out var pipeline))
            pipeline.Shutdown();
    }

    /// <summary>Flush a pipeline's channels.</summary>
    public static void Flush(int handle)
    {
        var pipeline = Resolve(handle);
        pipeline?.Flush();
    }

    /// <summary>Merge layer-4 pre-init overrides into the loaded config.</summary>
    private static void ApplyOverridesToConfig(OTelConfig config)
    {
        foreach (var (fullKey, value) in _preInitOverrides)
        {
            int dot = fullKey.IndexOf('.');
            if (dot < 0) continue;
            string section = fullKey[..dot];
            string key = fullKey[(dot + 1)..];

            switch (section.ToLowerInvariant())
            {
                case "resource":
                    config.Resource[key] = value;
                    break;
                case "pipeline":
                    if (key.Equals("emitter", StringComparison.OrdinalIgnoreCase))
                        config.DefaultEmitter = value;
                    else if (key.Equals("emitter.version", StringComparison.OrdinalIgnoreCase))
                        config.DefaultEmitterVersion = value;
                    break;
                case "batch":
                    ApplyBatchOverride(config.Batch, key, value);
                    break;
                case "destination":
                    // key = "otlp.endpoint" → destType="otlp", prop="endpoint"
                    int destDot = key.IndexOf('.');
                    if (destDot > 0)
                    {
                        string destType = key[..destDot];
                        string prop = key[(destDot + 1)..];
                        var destConfig = config.Destinations.Find(d => d.Type.Equals(destType, StringComparison.OrdinalIgnoreCase));
                        if (destConfig == null)
                        {
                            destConfig = new DestinationConfig { Type = destType };
                            config.Destinations.Add(destConfig);
                        }
                        if (prop.Equals("signals", StringComparison.OrdinalIgnoreCase))
                            destConfig.Signals = new HashSet<string>(value.Split(',').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
                        else
                            destConfig.Properties[prop] = value;
                    }
                    break;
                default:
                    // emitter.* sections
                    if (fullKey.StartsWith("emitter.", StringComparison.OrdinalIgnoreCase))
                    {
                        // fullKey = "emitter.mylib.version" → section="emitter", key="mylib.version"
                        // We need to parse: "emitter.<name>.version"
                        string remainder = fullKey["emitter.".Length..]; // "mylib.version"
                        int lastDot = remainder.LastIndexOf('.');
                        if (lastDot > 0)
                        {
                            string emitterName = remainder[..lastDot];
                            string prop = remainder[(lastDot + 1)..];
                            if (prop.Equals("version", StringComparison.OrdinalIgnoreCase))
                                config.EmitterRegistry[emitterName] = value;
                        }
                    }
                    break;
            }
        }
        _preInitOverrides.Clear();
    }

    private static void ApplyBatchOverride(BatchConfig batch, string key, string value)
    {
        if (!int.TryParse(value, out int intVal)) return;
        switch (key.ToLowerInvariant())
        {
            case "log.size": batch.LogSize = intVal; break;
            case "log.interval": batch.LogIntervalMs = intVal; break;
            case "span.size": batch.SpanSize = intVal; break;
            case "span.interval": batch.SpanIntervalMs = intVal; break;
            case "metric.size": batch.MetricSize = intVal; break;
            case "metric.interval": batch.MetricIntervalMs = intVal; break;
        }
    }

    private static Pipeline BuildPipeline(OTelConfig config)
    {
        var destinations = new List<IDestination>();

        foreach (var destConfig in config.Destinations)
        {
            var dest = DestinationFactory.Create(destConfig);
            if (dest != null)
                destinations.Add(dest);
        }

        // If no destinations configured, add a console fallback
        if (destinations.Count == 0)
            destinations.Add(new ConsoleDestination());

        var pipeline = new Pipeline(destinations, config.Batch);

        // Set resource attributes
        var autoDetected = ResourceDetector.Detect();
        pipeline.Resource = ResourceDetector.Merge(autoDetected, config.Resource);

        // Set emitter defaults and registry
        pipeline.DefaultEmitter = config.DefaultEmitter;
        pipeline.DefaultEmitterVersion = config.DefaultEmitterVersion;
        foreach (var (name, version) in config.EmitterRegistry)
            pipeline.EmitterRegistry[name] = version;

        // Inject resource attributes into destinations that need them
        foreach (var dest in destinations)
        {
            if (dest is IResourceAwareDestination resourceAware)
                resourceAware.SetResource(pipeline.Resource);
            if (dest is IEmitterAwareDestination emitterAware)
                emitterAware.SetEmitterConfig(pipeline.DefaultEmitter, pipeline.DefaultEmitterVersion, pipeline.EmitterRegistry);
        }

        return pipeline;
    }

    private enum PipelineState
    {
        Unconfigured,
        Configuring,
        Running
    }

    /// <summary>
    /// Best-effort shutdown of all active pipelines. Called from ProcessExit handler.
    /// Uses EmergencyDrain which synchronously flushes on the calling thread —
    /// avoids relying on background threads (which may be blocked by loader lock
    /// during DLL_PROCESS_DETACH on Windows).
    /// </summary>
    private static void ShutdownAll()
    {
        try
        {
            if (_singleton != null && _singletonState == PipelineState.Running)
            {
                _singleton.EmergencyDrain();
                _singleton = null;
                _singletonState = PipelineState.Unconfigured;
            }

            foreach (var (handle, pipeline) in _pipelines)
            {
                pipeline.EmergencyDrain();
            }
            _pipelines.Clear();
        }
        catch
        {
            // Swallow — we're in process teardown, nothing useful to do with errors
        }
    }

    /// <summary>
    /// Check whether ProcessExit auto-shutdown is active. Used for diagnostics.
    /// </summary>
    internal static bool ProcessExitRegistered => true;

    private static unsafe void RegisterNativeAtexit()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            _ = atexit(&OnNativeProcessExit);
        }
        catch
        {
            // Best-effort fallback: AppDomain.ProcessExit remains registered.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNativeProcessExit()
    {
        ShutdownAll();
    }

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "atexit")]
    private static unsafe extern int atexit(delegate* unmanaged[Cdecl]<void> callback);
}
