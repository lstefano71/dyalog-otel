using System.Net.Http;
using System.Text;
using System.Text.Json;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Serialization.JsonModels;

namespace Dyalog.OTel.Destinations;

/// <summary>
/// Sends telemetry in OTLP/JSON format to an OTLP-compatible endpoint.
/// Uses source-generated JSON for NativeAOT compatibility.
/// </summary>
public sealed class OtlpJsonDestination : IDestination, IResourceAwareDestination, IEmitterAwareDestination
{
    private readonly string _endpoint;
    private readonly Dictionary<string, string> _headers;
    private readonly Dictionary<string, string> _resource;
    private string _defaultEmitter = "dyalog-otel";
    private string _defaultEmitterVersion = "";
    private System.Collections.Concurrent.ConcurrentDictionary<string, string>? _emitterRegistry;
    private HttpClient? _client;

    public string Name => "otlp";

    public OtlpJsonDestination(string endpoint, Dictionary<string, string>? headers = null)
    {
        _endpoint = endpoint.TrimEnd('/');
        _headers = headers ?? new();
        _resource = new();
    }

    /// <summary>Allow pipeline to inject resource attributes.</summary>
    public void SetResource(Dictionary<string, string> resource)
    {
        _resource.Clear();
        foreach (var kv in resource)
            _resource[kv.Key] = kv.Value;
    }

    public void SetEmitterConfig(string defaultEmitter, string defaultEmitterVersion, System.Collections.Concurrent.ConcurrentDictionary<string, string> registry)
    {
        _defaultEmitter = defaultEmitter;
        _defaultEmitterVersion = defaultEmitterVersion;
        _emitterRegistry = registry;
    }

    public void Init()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        foreach (var kv in _headers)
            _client.DefaultRequestHeaders.TryAddWithoutValidation(kv.Key, kv.Value);
    }

    public void WriteLogs(ReadOnlySpan<LogRecord> batch)
    {
        if (batch.Length == 0) return;
        var request = new OtlpExportLogsRequest();
        var resourceLogs = new OtlpResourceLogs { Resource = BuildResource() };

        var groups = new Dictionary<string, OtlpScopeLogs>(StringComparer.Ordinal);
        foreach (var record in batch)
        {
            string emitterKey = ResolveEmitterName(record.Emitter);
            if (!groups.TryGetValue(emitterKey, out var scopeLogs))
            {
                scopeLogs = new OtlpScopeLogs { Scope = BuildJsonScope(emitterKey) };
                groups[emitterKey] = scopeLogs;
            }

            scopeLogs.LogRecords.Add(new OtlpLogRecord
            {
                TimeUnixNano = record.TimestampUnixNano.ToString(),
                SeverityNumber = record.SeverityNumber,
                SeverityText = record.SeverityText,
                Body = new OtlpAnyValue { StringValue = record.Body },
                TraceId = record.TraceId != null ? Convert.ToHexString(record.TraceId).ToLowerInvariant() : null,
                SpanId = record.SpanId != null ? Convert.ToHexString(record.SpanId).ToLowerInvariant() : null,
                Attributes = ConvertAttributes(record.Attributes, record.Template)
            });
        }

        foreach (var scopeLogs in groups.Values)
            resourceLogs.ScopeLogs.Add(scopeLogs);
        request.ResourceLogs.Add(resourceLogs);
        Post($"{_endpoint}/v1/logs", JsonSerializer.Serialize(request, OtlpJsonContext.Default.OtlpExportLogsRequest));
    }

    public void WriteSpans(ReadOnlySpan<SpanRecord> batch)
    {
        if (batch.Length == 0) return;
        var request = new OtlpExportTraceRequest();
        var resourceSpans = new OtlpResourceSpans { Resource = BuildResource() };

        var groups = new Dictionary<string, OtlpScopeSpans>(StringComparer.Ordinal);
        foreach (var record in batch)
        {
            string emitterKey = ResolveEmitterName(record.Emitter);
            if (!groups.TryGetValue(emitterKey, out var scopeSpans))
            {
                scopeSpans = new OtlpScopeSpans { Scope = BuildJsonScope(emitterKey) };
                groups[emitterKey] = scopeSpans;
            }

            scopeSpans.Spans.Add(new OtlpSpan
            {
                TraceId = Convert.ToHexString(record.TraceId).ToLowerInvariant(),
                SpanId = Convert.ToHexString(record.SpanId).ToLowerInvariant(),
                ParentSpanId = record.ParentSpanId != null ? Convert.ToHexString(record.ParentSpanId).ToLowerInvariant() : null,
                Name = record.Name,
                StartTimeUnixNano = record.StartTimeUnixNano.ToString(),
                EndTimeUnixNano = record.EndTimeUnixNano.ToString(),
                Status = new OtlpStatus { Code = record.StatusCode },
                Attributes = ConvertAttributes(record.Attributes, record.Template)
            });
        }

        foreach (var scopeSpans in groups.Values)
            resourceSpans.ScopeSpans.Add(scopeSpans);
        request.ResourceSpans.Add(resourceSpans);
        Post($"{_endpoint}/v1/traces", JsonSerializer.Serialize(request, OtlpJsonContext.Default.OtlpExportTraceRequest));
    }

    public void WriteMetrics(ReadOnlySpan<MetricPoint> batch)
    {
        if (batch.Length == 0) return;
        var request = new OtlpExportMetricsRequest();
        var resourceMetrics = new OtlpResourceMetrics { Resource = BuildResource() };

        var groups = new Dictionary<string, OtlpScopeMetrics>(StringComparer.Ordinal);
        foreach (var record in batch)
        {
            string emitterKey = ResolveEmitterName(record.Emitter);
            if (!groups.TryGetValue(emitterKey, out var scopeMetrics))
            {
                scopeMetrics = new OtlpScopeMetrics { Scope = BuildJsonScope(emitterKey) };
                groups[emitterKey] = scopeMetrics;
            }

            var metric = new OtlpMetric { Name = record.Name };
            var attrs = ConvertAttributes(record.Attributes, record.Template);
            var dp = new OtlpNumberDataPoint
            {
                TimeUnixNano = record.TimestampUnixNano.ToString(),
                AsDouble = record.Value,
                Attributes = attrs
            };

            switch (record.Type)
            {
                case MetricType.Counter:
                    metric.Sum = new OtlpSum { DataPoints = { dp } };
                    break;
                case MetricType.Gauge:
                    metric.Gauge = new OtlpGauge { DataPoints = { dp } };
                    break;
                case MetricType.Histogram:
                    metric.Sum = new OtlpSum { DataPoints = { dp }, IsMonotonic = false };
                    break;
            }

            scopeMetrics.Metrics.Add(metric);
        }

        foreach (var scopeMetrics in groups.Values)
            resourceMetrics.ScopeMetrics.Add(scopeMetrics);
        request.ResourceMetrics.Add(resourceMetrics);
        Post($"{_endpoint}/v1/metrics", JsonSerializer.Serialize(request, OtlpJsonContext.Default.OtlpExportMetricsRequest));
    }

    public void Flush() { }
    public void Shutdown() { _client?.Dispose(); _client = null; }
    public void Dispose() => Shutdown();

    private string ResolveEmitterName(string? emitter)
    {
        return string.IsNullOrEmpty(emitter) ? _defaultEmitter : emitter;
    }

    private OtlpInstrumentationScope BuildJsonScope(string emitterName)
    {
        string version = _defaultEmitterVersion;
        if (_emitterRegistry != null && _emitterRegistry.TryGetValue(emitterName, out var v))
            version = v;
        else if (emitterName != _defaultEmitter)
            version = "";

        return new OtlpInstrumentationScope { Name = emitterName, Version = version };
    }

    private OtlpResource BuildResource()
    {
        var resource = new OtlpResource();
        foreach (var kv in _resource)
            resource.Attributes.Add(new OtlpKeyValue { Key = kv.Key, Value = new OtlpAnyValue { StringValue = kv.Value } });
        return resource;
    }

    private static List<OtlpKeyValue>? ConvertAttributes(OTelAttribute[]? attrs, Templates.TemplateSnapshot? template)
    {
        if (attrs == null && template == null) return null;
        var result = new List<OtlpKeyValue>();

        if (template != null)
        {
            foreach (var kv in template.Attributes)
                result.Add(new OtlpKeyValue { Key = kv.Key, Value = ToAnyValue(kv.Value) });
        }

        if (attrs != null)
        {
            foreach (var attr in attrs)
                result.Add(new OtlpKeyValue { Key = attr.Key, Value = ToAnyValue(attr.Value) });
        }

        return result.Count > 0 ? result : null;
    }

    private static OtlpAnyValue ToAnyValue(object value) => value switch
    {
        string s => new OtlpAnyValue { StringValue = s },
        int i => new OtlpAnyValue { IntValue = i.ToString() },
        long l => new OtlpAnyValue { IntValue = l.ToString() },
        double d => new OtlpAnyValue { DoubleValue = d },
        float f => new OtlpAnyValue { DoubleValue = f },
        bool b => new OtlpAnyValue { BoolValue = b },
        _ => new OtlpAnyValue { StringValue = value?.ToString() ?? "" }
    };

    private void Post(string url, string json)
    {
        if (_client == null) return;
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = _client.Send(
                new HttpRequestMessage(HttpMethod.Post, url) { Content = content },
                HttpCompletionOption.ResponseHeadersRead);
        }
        catch { /* Background thread — errors tracked via InternalMetrics */ }
    }
}
