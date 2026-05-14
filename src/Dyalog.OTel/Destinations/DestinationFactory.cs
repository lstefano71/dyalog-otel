using Dyalog.OTel.Channels;
using Dyalog.OTel.Config;

namespace Dyalog.OTel.Destinations;

/// <summary>
/// Factory to create destination instances from config.
/// </summary>
internal static class DestinationFactory
{
    public static IDestination? Create(DestinationConfig config)
    {
        return config.Type.ToLowerInvariant() switch
        {
            "file" => CreateFile(config),
            "http" => CreateHttp(config),
            "otlp" => CreateOtlp(config),
            "console" => new ConsoleDestination(),
            _ => UnknownType(config.Type)
        };
    }

    private static OtlpJsonDestination CreateOtlp(DestinationConfig config)
    {
        string endpoint = config.Properties.GetValueOrDefault("endpoint", "http://localhost:4318");
        Dictionary<string, string>? headers = null;
        if (config.Properties.TryGetValue("headers", out var headerStr))
        {
            headers = new();
            foreach (var pair in headerStr.Split(',', StringSplitOptions.TrimEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0)
                    headers[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
            }
        }
        return new OtlpJsonDestination(endpoint, headers);
    }

    private static IDestination? UnknownType(string type)
    {
        Console.Error.WriteLine($"[dyalog-otel] WARNING: Unknown destination type '{type}'. Supported: file, http, otlp, console.");
        return null;
    }

    private static JsonlFileDestination CreateFile(DestinationConfig config)
    {
        string rawPath = config.Properties.GetValueOrDefault("path", "otel.jsonl");
        string path = Path.GetFullPath(rawPath);
        string rotateStr = config.Properties.GetValueOrDefault("rotate", "monthly");
        var rotation = rotateStr.ToLowerInvariant() switch
        {
            "hourly" => RotationPeriod.Hourly,
            "daily" => RotationPeriod.Daily,
            "monthly" => RotationPeriod.Monthly,
            "none" => RotationPeriod.None,
            _ => RotationPeriod.Monthly
        };
        return new JsonlFileDestination(path, rotation, config.Signals);
    }

    private static JsonlHttpDestination CreateHttp(DestinationConfig config)
    {
        string url = config.Properties.GetValueOrDefault("url", "http://localhost:8080");
        Dictionary<string, string>? headers = null;
        if (config.Properties.TryGetValue("headers", out var headerStr))
        {
            headers = new();
            foreach (var pair in headerStr.Split(',', StringSplitOptions.TrimEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0)
                    headers[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
            }
        }
        return new JsonlHttpDestination(url, config.Signals, headers);
    }
}

/// <summary>
/// Fallback destination that writes to stderr. Used when no destinations are configured.
/// </summary>
internal sealed class ConsoleDestination : IDestination
{
    public string Name => "console";
    public void Init() { }
    public void WriteLogs(ReadOnlySpan<LogRecord> batch)
    {
        foreach (var r in batch)
            Console.Error.WriteLine($"[LOG {r.SeverityText}] {r.Body}");
    }
    public void WriteSpans(ReadOnlySpan<SpanRecord> batch)
    {
        foreach (var r in batch)
            Console.Error.WriteLine($"[SPAN] {r.Name} ({r.EndTimeUnixNano - r.StartTimeUnixNano}ns)");
    }
    public void WriteMetrics(ReadOnlySpan<MetricPoint> batch)
    {
        foreach (var r in batch)
            Console.Error.WriteLine($"[METRIC {r.Type}] {r.Name}={r.Value}");
    }
    public void Flush() { }
    public void Shutdown() { }
    public void Dispose() { }
}
