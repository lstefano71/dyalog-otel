using Dyalog.OTel.Diagnostics;

namespace Dyalog.OTel.Config;

/// <summary>
/// Loads configuration from the 4-layer precedence:
/// 1. Hardcoded defaults
/// 2. Config file (INI)
/// 3. OTEL_* environment variables
/// 4. Builder calls (applied separately by PipelineBuilder)
///
/// Search order for config file:
///   DYALOG_OTEL_CONFIG env var → otel.ini beside DLL → otel.ini in CWD → defaults
/// </summary>
public static class ConfigLoader
{
    private const string ConfigFileName = "otel.ini";
    private const string ConfigEnvVar = "DYALOG_OTEL_CONFIG";

    public static OTelConfig Load()
    {
        var config = new OTelConfig();

        // Layer 2: Config file
        string? configPath = FindConfigFile();
        if (configPath != null)
            ApplyIniFile(config, configPath);

        // Layer 3: Standard OTEL_* environment variables
        ApplyEnvironmentVariables(config);

        return config;
    }

    private static string? FindConfigFile()
    {
        // 1. Explicit env var
        string? envPath = Environment.GetEnvironmentVariable(ConfigEnvVar);
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;

        // 2. Beside the loaded core DLL
        string baseDir = ModulePathResolver.GetModuleDirectory();
        string besideBase = Path.Combine(baseDir, ConfigFileName);
        if (File.Exists(besideBase))
            return besideBase;

        // 4. Current working directory
        string cwd = Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);
        if (File.Exists(cwd))
            return cwd;

        return null;
    }

    private static void ApplyIniFile(OTelConfig config, string path)
    {
        var ini = IniParser.ParseFile(path);

        // [resource] section
        if (ini.TryGetValue("resource", out var resourceSection))
        {
            foreach (var kv in resourceSection)
                config.Resource[kv.Key] = kv.Value;
        }

        // [destination.*] sections
        foreach (var (sectionName, props) in ini)
        {
            if (!sectionName.StartsWith("destination.", StringComparison.OrdinalIgnoreCase))
                continue;

            string type = sectionName["destination.".Length..];
            var dest = new DestinationConfig { Type = type, Properties = new(props) };

            if (props.TryGetValue("signals", out var signals))
            {
                dest.Signals = new HashSet<string>(
                    signals.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.OrdinalIgnoreCase);
                dest.Properties.Remove("signals");
            }

            config.Destinations.Add(dest);
        }

        // [batch] section
        if (ini.TryGetValue("batch", out var batchSection))
        {
            if (batchSection.TryGetValue("log.size", out var ls) && int.TryParse(ls, out var logSize))
                config.Batch.LogSize = logSize;
            if (batchSection.TryGetValue("log.interval", out var li) && int.TryParse(li, out var logInterval))
                config.Batch.LogIntervalMs = logInterval;
            if (batchSection.TryGetValue("span.size", out var ss) && int.TryParse(ss, out var spanSize))
                config.Batch.SpanSize = spanSize;
            if (batchSection.TryGetValue("span.interval", out var si) && int.TryParse(si, out var spanInterval))
                config.Batch.SpanIntervalMs = spanInterval;
            if (batchSection.TryGetValue("metric.size", out var ms) && int.TryParse(ms, out var metricSize))
                config.Batch.MetricSize = metricSize;
            if (batchSection.TryGetValue("metric.interval", out var mi) && int.TryParse(mi, out var metricInterval))
                config.Batch.MetricIntervalMs = metricInterval;
        }
    }

    private static void ApplyEnvironmentVariables(OTelConfig config)
    {
        // Service name
        string? serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");
        if (!string.IsNullOrEmpty(serviceName))
            config.Resource["service.name"] = serviceName;

        // Resource attributes (comma-separated key=value)
        string? resourceAttrs = Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES");
        if (!string.IsNullOrEmpty(resourceAttrs))
        {
            foreach (var pair in resourceAttrs.Split(',', StringSplitOptions.TrimEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0)
                    config.Resource[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
            }
        }

        // OTLP endpoint — add/override an OTLP destination
        string? otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            string protocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL") ?? "http/protobuf";
            var existing = config.Destinations.Find(d => d.Type.Equals("otlp", StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Properties["endpoint"] = otlpEndpoint;
                existing.Properties["protocol"] = protocol;
            }
            else
            {
                config.Destinations.Add(new DestinationConfig
                {
                    Type = "otlp",
                    Properties = new() { ["endpoint"] = otlpEndpoint, ["protocol"] = protocol }
                });
            }

            string? headers = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS");
            if (!string.IsNullOrEmpty(headers))
            {
                var dest = config.Destinations.Find(d => d.Type.Equals("otlp", StringComparison.OrdinalIgnoreCase))!;
                dest.Properties["headers"] = headers;
            }
        }

        // Batch config from env vars
        if (int.TryParse(Environment.GetEnvironmentVariable("OTEL_BSP_MAX_EXPORT_BATCH_SIZE"), out var batchSize))
        {
            config.Batch.LogSize = batchSize;
            config.Batch.SpanSize = batchSize;
            config.Batch.MetricSize = batchSize;
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("OTEL_BSP_SCHEDULE_DELAY"), out var scheduleDelay))
        {
            config.Batch.LogIntervalMs = scheduleDelay;
            config.Batch.SpanIntervalMs = scheduleDelay;
            config.Batch.MetricIntervalMs = scheduleDelay;
        }
    }
}
