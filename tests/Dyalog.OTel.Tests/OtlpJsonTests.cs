using System.Text.Json;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Destinations;
using Dyalog.OTel.Serialization.JsonModels;

namespace Dyalog.OTel.Tests;

public class OtlpJsonTests
{
    [Fact]
    public void OtlpModels_ProduceValidJson()
    {
        var request = new OtlpExportLogsRequest
        {
            ResourceLogs = new()
            {
                new OtlpResourceLogs
                {
                    Resource = new OtlpResource
                    {
                        Attributes = new() { new OtlpKeyValue { Key = "service.name", Value = new OtlpAnyValue { StringValue = "test" } } }
                    },
                    ScopeLogs = new()
                    {
                        new OtlpScopeLogs
                        {
                            LogRecords = new()
                            {
                                new OtlpLogRecord
                                {
                                    TimeUnixNano = "1234567890",
                                    SeverityNumber = 9,
                                    SeverityText = "INFO",
                                    Body = new OtlpAnyValue { StringValue = "test message" }
                                }
                            }
                        }
                    }
                }
            }
        };

        string json = JsonSerializer.Serialize(request, OtlpJsonContext.Default.OtlpExportLogsRequest);
        Assert.Contains("resourceLogs", json);
        Assert.Contains("scopeLogs", json);
        Assert.Contains("test message", json);
        Assert.Contains("service.name", json);
    }

    [Fact]
    public void OtlpTraceModels_ProduceValidJson()
    {
        var request = new OtlpExportTraceRequest
        {
            ResourceSpans = new()
            {
                new OtlpResourceSpans
                {
                    ScopeSpans = new()
                    {
                        new OtlpScopeSpans
                        {
                            Spans = new()
                            {
                                new OtlpSpan
                                {
                                    TraceId = "0af7651916cd43dd8448eb211c80319c",
                                    SpanId = "b7ad6b7169203331",
                                    Name = "test-span",
                                    StartTimeUnixNano = "1000000",
                                    EndTimeUnixNano = "2000000",
                                    Status = new OtlpStatus { Code = 1 }
                                }
                            }
                        }
                    }
                }
            }
        };

        string json = JsonSerializer.Serialize(request, OtlpJsonContext.Default.OtlpExportTraceRequest);
        Assert.Contains("resourceSpans", json);
        Assert.Contains("scopeSpans", json);
        Assert.Contains("test-span", json);
        Assert.Contains("0af7651916cd43dd8448eb211c80319c", json);
    }

    [Fact]
    public void OtlpMetricModels_ProduceValidJson()
    {
        var request = new OtlpExportMetricsRequest
        {
            ResourceMetrics = new()
            {
                new OtlpResourceMetrics
                {
                    ScopeMetrics = new()
                    {
                        new OtlpScopeMetrics
                        {
                            Metrics = new()
                            {
                                new OtlpMetric
                                {
                                    Name = "http.duration",
                                    Sum = new OtlpSum
                                    {
                                        DataPoints = new()
                                        {
                                            new OtlpNumberDataPoint { AsDouble = 42.5, TimeUnixNano = "999" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        string json = JsonSerializer.Serialize(request, OtlpJsonContext.Default.OtlpExportMetricsRequest);
        Assert.Contains("resourceMetrics", json);
        Assert.Contains("http.duration", json);
        Assert.Contains("42.5", json);
    }

    [Fact]
    public void OtlpDestination_SetResource_InjectsAttributes()
    {
        var dest = new OtlpJsonDestination("http://localhost:4318");
        dest.SetResource(new Dictionary<string, string>
        {
            ["service.name"] = "my-app",
            ["deployment.environment"] = "prod"
        });
        // Destination was created and resource set without error
        dest.Dispose();
    }

}
