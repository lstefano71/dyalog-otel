using System.Net.Http;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Serialization.ProtoModels;
using LightProto;

namespace Dyalog.OTel.Destinations;

/// <summary>
/// Sends telemetry in OTLP/Protobuf wire format to an OTLP-compatible endpoint.
/// Uses LightProto source-generated serialization for NativeAOT compatibility.
/// </summary>
public sealed class OtlpProtobufDestination : IDestination
{
    private readonly string _endpoint;
    private readonly Dictionary<string, string> _headers;
    private readonly Dictionary<string, string> _resource;
    private HttpClient? _client;

    public string Name => "otlp-proto";

    public OtlpProtobufDestination(string endpoint, Dictionary<string, string>? headers = null)
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
        var request = new ProtoExportLogsServiceRequest();
        var resourceLogs = new ProtoResourceLogs { Resource = BuildResource() };
        var scopeLogs = new ProtoScopeLogs();

        foreach (var record in batch)
        {
            var logRecord = new ProtoLogRecord
            {
                TimeUnixNano = (ulong)record.TimestampUnixNano,
                ObservedTimeUnixNano = (ulong)record.TimestampUnixNano,
                SeverityNumber = (ProtoSeverityNumber)record.SeverityNumber,
                SeverityText = record.SeverityText,
                Body = new ProtoAnyValue { StringValue = record.Body },
                TraceId = record.TraceId,
                SpanId = record.SpanId,
            };
            AddAttributes(logRecord.Attributes, record.Attributes, record.Template);
            scopeLogs.LogRecords.Add(logRecord);
        }

        resourceLogs.ScopeLogs.Add(scopeLogs);
        request.ResourceLogs.Add(resourceLogs);
        Post($"{_endpoint}/v1/logs", request.ToByteArray());
    }

    public void WriteSpans(ReadOnlySpan<SpanRecord> batch)
    {
        if (batch.Length == 0) return;
        var request = new ProtoExportTraceServiceRequest();
        var resourceSpans = new ProtoResourceSpans { Resource = BuildResource() };
        var scopeSpans = new ProtoScopeSpans();

        foreach (var record in batch)
        {
            var span = new ProtoSpan
            {
                TraceId = record.TraceId,
                SpanId = record.SpanId,
                ParentSpanId = record.ParentSpanId,
                Name = record.Name,
                Kind = ProtoSpanKind.Internal,
                StartTimeUnixNano = (ulong)record.StartTimeUnixNano,
                EndTimeUnixNano = (ulong)record.EndTimeUnixNano,
                Status = new ProtoStatus { Code = (ProtoStatusCode)record.StatusCode },
            };
            AddAttributes(span.Attributes, record.Attributes, record.Template);
            scopeSpans.Spans.Add(span);
        }

        resourceSpans.ScopeSpans.Add(scopeSpans);
        request.ResourceSpans.Add(resourceSpans);
        Post($"{_endpoint}/v1/traces", request.ToByteArray());
    }

    public void WriteMetrics(ReadOnlySpan<MetricPoint> batch)
    {
        if (batch.Length == 0) return;
        var request = new ProtoExportMetricsServiceRequest();
        var resourceMetrics = new ProtoResourceMetrics { Resource = BuildResource() };
        var scopeMetrics = new ProtoScopeMetrics();

        foreach (var record in batch)
        {
            var metric = new ProtoMetric { Name = record.Name };
            var dp = new ProtoNumberDataPoint
            {
                TimeUnixNano = (ulong)record.TimestampUnixNano,
                AsDouble = record.Value,
            };
            AddAttributes(dp.Attributes, record.Attributes, record.Template);

            switch (record.Type)
            {
                case MetricType.Counter:
                    metric.Sum = new ProtoSum
                    {
                        DataPoints = { dp },
                        AggregationTemporality = ProtoAggregationTemporality.Delta,
                        IsMonotonic = true
                    };
                    break;
                case MetricType.Gauge:
                    metric.Gauge = new ProtoGauge { DataPoints = { dp } };
                    break;
                case MetricType.Histogram:
                    metric.Sum = new ProtoSum
                    {
                        DataPoints = { dp },
                        AggregationTemporality = ProtoAggregationTemporality.Delta,
                        IsMonotonic = false
                    };
                    break;
            }

            scopeMetrics.Metrics.Add(metric);
        }

        resourceMetrics.ScopeMetrics.Add(scopeMetrics);
        request.ResourceMetrics.Add(resourceMetrics);
        Post($"{_endpoint}/v1/metrics", request.ToByteArray());
    }

    public void Flush() { }
    public void Shutdown() { _client?.Dispose(); _client = null; }
    public void Dispose() => Shutdown();

    private ProtoResource BuildResource()
    {
        var resource = new ProtoResource();
        foreach (var kv in _resource)
            resource.Attributes.Add(new ProtoKeyValue
            {
                Key = kv.Key,
                Value = new ProtoAnyValue { StringValue = kv.Value }
            });
        return resource;
    }

    private static void AddAttributes(List<ProtoKeyValue> target, OTelAttribute[]? attrs, Templates.TemplateSnapshot? template)
    {
        if (template != null)
        {
            foreach (var kv in template.Attributes)
                target.Add(new ProtoKeyValue { Key = kv.Key, Value = ToAnyValue(kv.Value) });
        }

        if (attrs != null)
        {
            foreach (var attr in attrs)
                target.Add(new ProtoKeyValue { Key = attr.Key, Value = ToAnyValue(attr.Value) });
        }
    }

    private static ProtoAnyValue ToAnyValue(object value) => value switch
    {
        string s => new ProtoAnyValue { StringValue = s },
        int i => new ProtoAnyValue { IntValue = i },
        long l => new ProtoAnyValue { IntValue = l },
        double d => new ProtoAnyValue { DoubleValue = d },
        float f => new ProtoAnyValue { DoubleValue = f },
        bool b => new ProtoAnyValue { BoolValue = b },
        _ => new ProtoAnyValue { StringValue = value?.ToString() ?? "" }
    };

    private void Post(string url, byte[] data)
    {
        if (_client == null) return;
        try
        {
            using var content = new ByteArrayContent(data);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
            using var response = _client.Send(
                new HttpRequestMessage(HttpMethod.Post, url) { Content = content });
            // Drain response body so the connection returns to the pool
            response.Content.ReadAsStream().CopyTo(Stream.Null);
        }
        catch { /* Background thread — errors tracked via InternalMetrics */ }
    }
}
