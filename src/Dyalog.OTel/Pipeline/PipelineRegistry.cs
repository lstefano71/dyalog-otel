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
    /// Key = "section\x1Fkey", value = pending override record.
    /// </summary>
    private static readonly Dictionary<string, PendingConfigOverride> _preInitOverrides = new(StringComparer.OrdinalIgnoreCase);

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
    /// Pre-init: validates and stores override. Post-init: only emitter.* sections allowed (returns 2 if frozen).
    /// Returns: 0=ok, 1=unknown section/key, 2=frozen (post-init).
    /// </summary>
    public static int ApplyConfigOverride(string section, string key, string value)
    {
        lock (_initLock)
        {
            var kind = ClassifyConfigOverride(section, key);
            if (kind == ConfigOverrideKind.Unknown)
                return 1;

            if (_singletonState == PipelineState.Running)
            {
                if (kind != ConfigOverrideKind.MutableAfterInit)
                    return 2; // Frozen section

                string emitterName = section["emitter.".Length..];
                if (_singleton != null)
                {
                    _singleton.EmitterRegistry[emitterName] = value;
                    _singleton.ApplyEmitterConfigToDestinations(bestEffort: true);
                }
                return 0;
            }

            // Pre-init: accumulate for merge at init time
            string overrideId = $"{section}\x1F{key}";
            _preInitOverrides[overrideId] = new PendingConfigOverride(section, key, value);
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
        foreach (var pending in _preInitOverrides.Values)
        {
            ApplyOverrideToConfig(config, pending.Section, pending.Key, pending.Value);
        }
        _preInitOverrides.Clear();
    }

    private static void ApplyOverrideToConfig(OTelConfig config, string section, string key, string value)
    {
        switch (section.ToLowerInvariant())
        {
            case "resource":
                config.Resource[key] = value;
                return;
            case "pipeline":
                if (key.Equals("emitter", StringComparison.OrdinalIgnoreCase))
                    config.DefaultEmitter = value;
                else if (key.Equals("emitter.version", StringComparison.OrdinalIgnoreCase))
                    config.DefaultEmitterVersion = value;
                return;
            case "batch":
                ApplyBatchOverride(config.Batch, key, value);
                return;
        }

        if (section.StartsWith("destination.", StringComparison.OrdinalIgnoreCase))
        {
            string destType = section["destination.".Length..];
            var destConfig = config.Destinations.Find(d => d.Type.Equals(destType, StringComparison.OrdinalIgnoreCase));
            if (destConfig == null)
            {
                destConfig = new DestinationConfig { Type = destType };
                config.Destinations.Add(destConfig);
            }
            if (key.Equals("signals", StringComparison.OrdinalIgnoreCase))
                destConfig.Signals = new HashSet<string>(value.Split(',').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            else
                destConfig.Properties[key] = value;
            return;
        }

        if (section.StartsWith("emitter.", StringComparison.OrdinalIgnoreCase))
        {
            string emitterName = section["emitter.".Length..];
            config.EmitterRegistry[emitterName] = value;
        }
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
        }

        pipeline.ApplyEmitterConfigToDestinations(bestEffort: false);
        return pipeline;
    }

    private enum PipelineState
    {
        Unconfigured,
        Configuring,
        Running
    }

    private enum ConfigOverrideKind
    {
        Unknown,
        PreInitOnly,
        MutableAfterInit
    }

    private sealed record PendingConfigOverride(string Section, string Key, string Value);

    private static ConfigOverrideKind ClassifyConfigOverride(string section, string key)
    {
        if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key))
            return ConfigOverrideKind.Unknown;

        if (section.Equals("resource", StringComparison.OrdinalIgnoreCase))
            return ConfigOverrideKind.PreInitOnly; // resource.* is open-ended

        if (section.Equals("pipeline", StringComparison.OrdinalIgnoreCase))
        {
            return key.Equals("emitter", StringComparison.OrdinalIgnoreCase)
                || key.Equals("emitter.version", StringComparison.OrdinalIgnoreCase)
                ? ConfigOverrideKind.PreInitOnly
                : ConfigOverrideKind.Unknown;
        }

        if (section.Equals("batch", StringComparison.OrdinalIgnoreCase))
            return IsKnownBatchKey(key) ? ConfigOverrideKind.PreInitOnly : ConfigOverrideKind.Unknown;

        if (section.StartsWith("destination.", StringComparison.OrdinalIgnoreCase))
        {
            string destType = section["destination.".Length..];
            return !string.IsNullOrWhiteSpace(destType) && DestinationFactory.IsSupportedType(destType)
                ? ConfigOverrideKind.PreInitOnly
                : ConfigOverrideKind.Unknown;
        }

        if (section.StartsWith("emitter.", StringComparison.OrdinalIgnoreCase))
        {
            string emitterName = section["emitter.".Length..];
            return !string.IsNullOrWhiteSpace(emitterName) && key.Equals("version", StringComparison.OrdinalIgnoreCase)
                ? ConfigOverrideKind.MutableAfterInit
                : ConfigOverrideKind.Unknown;
        }

        return ConfigOverrideKind.Unknown;
    }

    private static bool IsKnownBatchKey(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "log.size" or "log.interval" or "span.size" or "span.interval" or "metric.size" or "metric.interval" => true,
            _ => false
        };
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
