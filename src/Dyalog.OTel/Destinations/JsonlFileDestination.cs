using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dyalog.OTel.Channels;

namespace Dyalog.OTel.Destinations;

/// <summary>
/// Writes JSONL (one JSON object per line) to a local file with time-based rotation.
/// Supports cooperative cross-process locking for shared network paths.
/// </summary>
public sealed class JsonlFileDestination : IDestination
{
    private readonly HashSet<string> _signals;
    private readonly CooperativeFileWriter _writer;

    public string Name => "file";
    public long DroppedBatches => _writer.DroppedBatches;

    public JsonlFileDestination(
        string basePath,
        RotationPeriod rotation,
        HashSet<string> signals,
        SharingMode sharing = SharingMode.Cooperative,
        FlushMode flush = FlushMode.Batch,
        int lockTimeoutMs = 5000,
        LockStyle lockStyle = LockStyle.Inline)
    {
        _signals = signals;
        _writer = new CooperativeFileWriter(basePath, rotation, sharing, flush, lockStyle, lockTimeoutMs);
    }

    public void Init()
    {
        _writer.Open();
    }

    public void WriteLogs(ReadOnlySpan<LogRecord> batch)
    {
        if (!_signals.Contains("log")) return;
        var sb = new StringBuilder(batch.Length * 128);
        foreach (var record in batch)
            sb.AppendLine(JsonSerializer.Serialize(record, JsonContext.Default.LogRecord));
        _writer.Write(sb);
    }

    public void WriteSpans(ReadOnlySpan<SpanRecord> batch)
    {
        if (!_signals.Contains("span")) return;
        var sb = new StringBuilder(batch.Length * 192);
        foreach (var record in batch)
            sb.AppendLine(JsonSerializer.Serialize(record, JsonContext.Default.SpanRecord));
        _writer.Write(sb);
    }

    public void WriteMetrics(ReadOnlySpan<MetricPoint> batch)
    {
        if (!_signals.Contains("metric")) return;
        var sb = new StringBuilder(batch.Length * 96);
        foreach (var record in batch)
            sb.AppendLine(JsonSerializer.Serialize(record, JsonContext.Default.MetricPoint));
        _writer.Write(sb);
    }

    public void Flush()
    {
        _writer.Flush();
    }

    public void Shutdown()
    {
        _writer.Shutdown();
    }

    public void Dispose() => Shutdown();
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
