using System.Text.Json.Serialization;

namespace Dyalog.OTel.Serialization.JsonModels;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OtlpExportLogsRequest))]
[JsonSerializable(typeof(OtlpExportTraceRequest))]
[JsonSerializable(typeof(OtlpExportMetricsRequest))]
internal partial class OtlpJsonContext : JsonSerializerContext;
