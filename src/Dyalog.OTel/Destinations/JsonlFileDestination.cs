using System.Text.Json;
using System.Text.Json.Serialization;
using Dyalog.OTel.Channels;

namespace Dyalog.OTel.Destinations;

/// <summary>
/// Writes JSONL (one JSON object per line) to a local file with time-based rotation.
/// </summary>
public sealed class JsonlFileDestination : IDestination
{
    private readonly string _basePath;
    private readonly RotationPeriod _rotation;
    private readonly HashSet<string> _signals;
    private readonly object _writeLock = new();
    private StreamWriter? _writer;
    private string? _currentFilePath;
    private string _currentPeriodKey = "";

    public string Name => "file";

    public JsonlFileDestination(string basePath, RotationPeriod rotation, HashSet<string> signals)
    {
        _basePath = basePath;
        _rotation = rotation;
        _signals = signals;
    }

    public void Init()
    {
        EnsureWriter();
    }

    public void WriteLogs(ReadOnlySpan<LogRecord> batch)
    {
        if (!_signals.Contains("log")) return;
        // Serialize outside the lock to minimize lock hold time
        var sb = new System.Text.StringBuilder(batch.Length * 128);
        foreach (var record in batch)
            sb.AppendLine(JsonSerializer.Serialize(record, JsonContext.Default.LogRecord));
        lock (_writeLock)
        {
            EnsureWriter();
            _writer!.Write(sb);
        }
    }

    public void WriteSpans(ReadOnlySpan<SpanRecord> batch)
    {
        if (!_signals.Contains("span")) return;
        var sb = new System.Text.StringBuilder(batch.Length * 192);
        foreach (var record in batch)
            sb.AppendLine(JsonSerializer.Serialize(record, JsonContext.Default.SpanRecord));
        lock (_writeLock)
        {
            EnsureWriter();
            _writer!.Write(sb);
        }
    }

    public void WriteMetrics(ReadOnlySpan<MetricPoint> batch)
    {
        if (!_signals.Contains("metric")) return;
        var sb = new System.Text.StringBuilder(batch.Length * 96);
        foreach (var record in batch)
            sb.AppendLine(JsonSerializer.Serialize(record, JsonContext.Default.MetricPoint));
        lock (_writeLock)
        {
            EnsureWriter();
            _writer!.Write(sb);
        }
    }

    public void Flush()
    {
        lock (_writeLock)
            _writer?.Flush();
    }

    public void Shutdown()
    {
        lock (_writeLock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    public void Dispose() => Shutdown();

    private void EnsureWriter()
    {
        string periodKey = GetPeriodKey(DateTime.UtcNow);
        if (periodKey == _currentPeriodKey && _writer != null)
            return;

        // Rotate
        _writer?.Flush();
        _writer?.Dispose();

        _currentPeriodKey = periodKey;
        _currentFilePath = BuildRotatedPath(periodKey);

        string? dir = Path.GetDirectoryName(_currentFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _writer = new StreamWriter(_currentFilePath, append: true) { AutoFlush = false };
    }

    private string BuildRotatedPath(string periodKey)
    {
        if (_rotation == RotationPeriod.None)
            return _basePath;

        string dir = Path.GetDirectoryName(_basePath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(_basePath);
        string ext = Path.GetExtension(_basePath);
        return Path.Combine(dir, $"{name}-{periodKey}{ext}");
    }

    private string GetPeriodKey(DateTime utc) => _rotation switch
    {
        RotationPeriod.Hourly => utc.ToString("yyyy-MM-dd-HH"),
        RotationPeriod.Daily => utc.ToString("yyyy-MM-dd"),
        RotationPeriod.Monthly => utc.ToString("yyyy-MM"),
        _ => ""
    };
}

/// <summary>
/// Source-generated JSON serialization context for NativeAOT compatibility.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LogRecord))]
[JsonSerializable(typeof(SpanRecord))]
[JsonSerializable(typeof(MetricPoint))]
[JsonSerializable(typeof(OTelAttribute))]
internal partial class JsonContext : JsonSerializerContext;
