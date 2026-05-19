using System.Text;
using Dyalog.OTel.Channels;

namespace Dyalog.OTel.PluginAbi;

public static unsafe class NativeTelemetryMaterializer
{
    public static LogRecord[] MaterializeLogs(NativeLogRecord* records, int count)
    {
        var result = new LogRecord[count];
        for (int i = 0; i < count; i++)
        {
            var record = records[i];
            result[i] = new LogRecord
            {
                TimestampUnixNano = record.TimestampUnixNano,
                SeverityNumber = record.SeverityNumber,
                SeverityText = ReadString(record.SeverityText),
                Body = ReadString(record.Body),
                TraceId = ReadBytes(record.TraceId),
                SpanId = ReadBytes(record.SpanId),
                Attributes = ReadAttributes(record.Attributes, record.AttributeCount),
                Emitter = ReadNullableString(record.Emitter)
            };
        }

        return result;
    }

    public static SpanRecord[] MaterializeSpans(NativeSpanRecord* records, int count)
    {
        var result = new SpanRecord[count];
        for (int i = 0; i < count; i++)
        {
            var record = records[i];
            result[i] = new SpanRecord
            {
                TraceId = ReadBytes(record.TraceId) ?? Array.Empty<byte>(),
                SpanId = ReadBytes(record.SpanId) ?? Array.Empty<byte>(),
                ParentSpanId = ReadBytes(record.ParentSpanId),
                Name = ReadString(record.Name),
                StartTimeUnixNano = record.StartTimeUnixNano,
                EndTimeUnixNano = record.EndTimeUnixNano,
                StatusCode = record.StatusCode,
                Attributes = ReadAttributes(record.Attributes, record.AttributeCount),
                Emitter = ReadNullableString(record.Emitter)
            };
        }

        return result;
    }

    public static MetricPoint[] MaterializeMetrics(NativeMetricPoint* records, int count)
    {
        var result = new MetricPoint[count];
        for (int i = 0; i < count; i++)
        {
            var record = records[i];
            result[i] = new MetricPoint
            {
                TimestampUnixNano = record.TimestampUnixNano,
                Name = ReadString(record.Name),
                Value = record.Value,
                Type = (MetricType)record.Type,
                Attributes = ReadAttributes(record.Attributes, record.AttributeCount),
                Emitter = ReadNullableString(record.Emitter)
            };
        }

        return result;
    }

    public static Dictionary<string, string> DecodePropertyBag(byte* blob, int length)
    {
        if (blob == null || length == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return PropertyBagCodec.Decode(new ReadOnlySpan<byte>(blob, length));
    }

    public static HashSet<string> ExtractSignals(Dictionary<string, string> properties)
    {
        if (!properties.TryGetValue("signals", out var rawSignals))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "log", "span", "metric" };

        properties.Remove("signals");
        return new HashSet<string>(
            rawSignals.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
    }

    private static OTelAttribute[]? ReadAttributes(NativeAttribute* attributes, int count)
    {
        if (attributes == null || count == 0)
            return null;

        var result = new OTelAttribute[count];
        for (int i = 0; i < count; i++)
        {
            var attribute = attributes[i];
            object value = attribute.Kind switch
            {
                NativeAttributeValueKind.String => ReadString(attribute.StringValue),
                NativeAttributeValueKind.Int64 => attribute.Int64Value,
                NativeAttributeValueKind.Double => attribute.DoubleValue,
                NativeAttributeValueKind.Bool => attribute.BoolValue != 0,
                _ => ReadString(attribute.StringValue)
            };

            result[i] = new OTelAttribute(ReadString(attribute.Key), value);
        }

        return result;
    }

    private static string ReadString(Utf8Span span)
    {
        if (span.Ptr == null || span.Length == 0)
            return "";

        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(span.Ptr, span.Length));
    }

    private static string? ReadNullableString(Utf8Span span)
    {
        if (span.Ptr == null || span.Length == 0)
            return null;

        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(span.Ptr, span.Length));
    }

    private static byte[]? ReadBytes(ByteSpan span)
    {
        if (span.Ptr == null || span.Length == 0)
            return null;

        return new ReadOnlySpan<byte>(span.Ptr, span.Length).ToArray();
    }
}
