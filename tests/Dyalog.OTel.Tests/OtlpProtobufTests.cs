using Dyalog.OTel.Channels;
using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;
using Dyalog.OTel.Serialization.ProtoModels;
using LightProto;

namespace Dyalog.OTel.Tests;

public class OtlpProtobufTests
{
    [Fact]
    public void ProtoLogModels_SerializeToNonEmptyBytes()
    {
        var request = new ProtoExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ProtoResourceLogs
                {
                    Resource = new ProtoResource
                    {
                        Attributes = { new ProtoKeyValue { Key = "service.name", Value = new ProtoAnyValue { StringValue = "test" } } }
                    },
                    ScopeLogs =
                    {
                        new ProtoScopeLogs
                        {
                            LogRecords =
                            {
                                new ProtoLogRecord
                                {
                                    TimeUnixNano = 1234567890,
                                    SeverityNumber = ProtoSeverityNumber.Info,
                                    SeverityText = "INFO",
                                    Body = new ProtoAnyValue { StringValue = "test message" }
                                }
                            }
                        }
                    }
                }
            }
        };

        byte[] bytes = request.ToByteArray();
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0, "Serialized protobuf should be non-empty");
    }

    [Fact]
    public void ProtoTraceModels_SerializeToNonEmptyBytes()
    {
        var request = new ProtoExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ProtoResourceSpans
                {
                    ScopeSpans =
                    {
                        new ProtoScopeSpans
                        {
                            Spans =
                            {
                                new ProtoSpan
                                {
                                    TraceId = new byte[16],
                                    SpanId = new byte[8],
                                    Name = "test-span",
                                    Kind = ProtoSpanKind.Internal,
                                    StartTimeUnixNano = 1000000,
                                    EndTimeUnixNano = 2000000,
                                    Status = new ProtoStatus { Code = ProtoStatusCode.Ok }
                                }
                            }
                        }
                    }
                }
            }
        };

        byte[] bytes = request.ToByteArray();
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0, "Serialized protobuf should be non-empty");
    }

    [Fact]
    public void ProtoMetricModels_SerializeToNonEmptyBytes()
    {
        var request = new ProtoExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ProtoResourceMetrics
                {
                    ScopeMetrics =
                    {
                        new ProtoScopeMetrics
                        {
                            Metrics =
                            {
                                new ProtoMetric
                                {
                                    Name = "http.duration",
                                    Sum = new ProtoSum
                                    {
                                        DataPoints = { new ProtoNumberDataPoint { AsDouble = 42.5, TimeUnixNano = 999 } },
                                        IsMonotonic = true,
                                        AggregationTemporality = ProtoAggregationTemporality.Delta
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        byte[] bytes = request.ToByteArray();
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0, "Serialized protobuf should be non-empty");
    }

    [Fact]
    public void ProtobufDestination_AcceptsBatchWriteWithoutError()
    {
        var dest = new OtlpProtobufDestination("http://localhost:4318");
        dest.SetResource(new Dictionary<string, string>
        {
            ["service.name"] = "test-app",
            ["deployment.environment"] = "test"
        });

        // WriteLogs/WriteSpans/WriteMetrics should not throw even without Init()
        // (Post is a no-op when _client is null)
        var logs = new[]
        {
            new LogRecord
            {
                TimestampUnixNano = 1234567890,
                SeverityNumber = 9,
                SeverityText = "INFO",
                Body = "test log",
                TraceId = new byte[16],
                SpanId = new byte[8]
            }
        };
        dest.WriteLogs(logs);

        var spans = new[]
        {
            new SpanRecord
            {
                TraceId = new byte[16],
                SpanId = new byte[8],
                Name = "test-span",
                StartTimeUnixNano = 1000000,
                EndTimeUnixNano = 2000000,
                StatusCode = 1
            }
        };
        dest.WriteSpans(spans);

        var metrics = new[]
        {
            new MetricPoint
            {
                TimestampUnixNano = 1234567890,
                Name = "test.metric",
                Value = 42.0,
                Type = MetricType.Counter
            }
        };
        dest.WriteMetrics(metrics);

        dest.Dispose();
    }
}
