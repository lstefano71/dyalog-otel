using Dyalog.OTel.Channels;
using Dyalog.OTel.Config;

namespace Dyalog.OTel.Destinations;

/// <summary>
/// Factory to create destination instances from config.
/// </summary>
internal static class DestinationFactory
{
    public static bool IsSupportedType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "text" or "console" or "file" or "http" or "otlp" => true,
            _ => false
        };
    }

    public static IDestination? Create(DestinationConfig config)
    {
        return config.Type.ToLowerInvariant() switch
        {
            "text" => CreateText(config),
            "console" => new ConsoleDestination(),
            "file" or "http" or "otlp" => DestinationFamilyLoader.Create(config),
            _ => UnknownType(config.Type)
        };
    }

    private static TextDestination CreateText(DestinationConfig config)
    {
        var options = TextDestinationConfig.Parse(config);
        return new TextDestination(options);
    }

    private static IDestination UnknownType(string type) =>
        throw new InvalidOperationException(
            $"Unknown destination type '{type}'. Supported: file, http, otlp, text, console.");
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
