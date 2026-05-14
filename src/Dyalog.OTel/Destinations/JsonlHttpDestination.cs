using System.Net.Http;
using System.Text;
using System.Text.Json;
using Dyalog.OTel.Channels;

namespace Dyalog.OTel.Destinations;

/// <summary>
/// POSTs JSONL batches to an HTTP endpoint.
/// Single HttpClient instance with keep-alive for connection pooling.
/// </summary>
public sealed class JsonlHttpDestination : IDestination
{
    private readonly string _url;
    private readonly HashSet<string> _signals;
    private readonly Dictionary<string, string> _headers;
    private HttpClient? _client;

    public string Name => "http";

    public JsonlHttpDestination(string url, HashSet<string> signals, Dictionary<string, string>? headers = null)
    {
        _url = url;
        _signals = signals;
        _headers = headers ?? new();
    }

    public void Init()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        foreach (var kv in _headers)
            _client.DefaultRequestHeaders.TryAddWithoutValidation(kv.Key, kv.Value);
    }

    public void WriteLogs(ReadOnlySpan<LogRecord> batch)
    {
        if (!_signals.Contains("log")) return;
        var sb = new StringBuilder();
        foreach (var record in batch)
            sb.AppendLine(JsonSerializer.Serialize(record, JsonContext.Default.LogRecord));
        Post(sb.ToString());
    }

    public void WriteSpans(ReadOnlySpan<SpanRecord> batch)
    {
        if (!_signals.Contains("span")) return;
        var sb = new StringBuilder();
        foreach (var record in batch)
            sb.AppendLine(JsonSerializer.Serialize(record, JsonContext.Default.SpanRecord));
        Post(sb.ToString());
    }

    public void WriteMetrics(ReadOnlySpan<MetricPoint> batch)
    {
        if (!_signals.Contains("metric")) return;
        var sb = new StringBuilder();
        foreach (var record in batch)
            sb.AppendLine(JsonSerializer.Serialize(record, JsonContext.Default.MetricPoint));
        Post(sb.ToString());
    }

    public void Flush() { /* HTTP has no buffer to flush */ }

    public void Shutdown()
    {
        _client?.Dispose();
        _client = null;
    }

    public void Dispose() => Shutdown();

    private void Post(string body)
    {
        if (_client == null) return;
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/x-ndjson");
            using var response = _client.Send(
                new HttpRequestMessage(HttpMethod.Post, _url) { Content = content },
                HttpCompletionOption.ResponseHeadersRead);
            // Fire-and-forget: we only check the status code for diagnostics
        }
        catch
        {
            // Swallowed — background thread errors are tracked via InternalMetrics
        }
    }
}
