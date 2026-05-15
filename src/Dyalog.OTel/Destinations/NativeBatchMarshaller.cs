using System.Runtime.InteropServices;
using System.Text;
using Dyalog.OTel.Channels;
using Dyalog.OTel.PluginAbi;
using Dyalog.OTel.Templates;

namespace Dyalog.OTel.Destinations;

internal static unsafe class NativeBatchMarshaller
{
    public static NativeBatchScope<NativeLogRecord> MarshalLogs(ReadOnlySpan<LogRecord> batch)
    {
        var allocations = new List<nint>();
        var records = AllocArray<NativeLogRecord>(allocations, batch.Length);
        for (int i = 0; i < batch.Length; i++)
        {
            var record = batch[i];
            records[i] = new NativeLogRecord
            {
                TimestampUnixNano = record.TimestampUnixNano,
                SeverityNumber = record.SeverityNumber,
                SeverityText = EncodeUtf8(allocations, record.SeverityText),
                Body = EncodeUtf8(allocations, record.Body),
                TraceId = EncodeBytes(allocations, record.TraceId),
                SpanId = EncodeBytes(allocations, record.SpanId),
                Attributes = EncodeAttributes(allocations, record.Template, record.Attributes, out int attributeCount),
                AttributeCount = attributeCount
            };
        }

        return new NativeBatchScope<NativeLogRecord>(records, batch.Length, allocations);
    }

    public static NativeBatchScope<NativeSpanRecord> MarshalSpans(ReadOnlySpan<SpanRecord> batch)
    {
        var allocations = new List<nint>();
        var records = AllocArray<NativeSpanRecord>(allocations, batch.Length);
        for (int i = 0; i < batch.Length; i++)
        {
            var record = batch[i];
            records[i] = new NativeSpanRecord
            {
                TraceId = EncodeBytes(allocations, record.TraceId),
                SpanId = EncodeBytes(allocations, record.SpanId),
                ParentSpanId = EncodeBytes(allocations, record.ParentSpanId),
                Name = EncodeUtf8(allocations, record.Name),
                StartTimeUnixNano = record.StartTimeUnixNano,
                EndTimeUnixNano = record.EndTimeUnixNano,
                StatusCode = record.StatusCode,
                Attributes = EncodeAttributes(allocations, record.Template, record.Attributes, out int attributeCount),
                AttributeCount = attributeCount
            };
        }

        return new NativeBatchScope<NativeSpanRecord>(records, batch.Length, allocations);
    }

    public static NativeBatchScope<NativeMetricPoint> MarshalMetrics(ReadOnlySpan<MetricPoint> batch)
    {
        var allocations = new List<nint>();
        var records = AllocArray<NativeMetricPoint>(allocations, batch.Length);
        for (int i = 0; i < batch.Length; i++)
        {
            var record = batch[i];
            records[i] = new NativeMetricPoint
            {
                TimestampUnixNano = record.TimestampUnixNano,
                Name = EncodeUtf8(allocations, record.Name),
                Value = record.Value,
                Type = (int)record.Type,
                Attributes = EncodeAttributes(allocations, record.Template, record.Attributes, out int attributeCount),
                AttributeCount = attributeCount
            };
        }

        return new NativeBatchScope<NativeMetricPoint>(records, batch.Length, allocations);
    }

    private static T* AllocArray<T>(List<nint> allocations, int count) where T : unmanaged
    {
        if (count == 0)
            return null;

        T* ptr = (T*)NativeMemory.Alloc((nuint)count, (nuint)sizeof(T));
        allocations.Add((nint)ptr);
        return ptr;
    }

    private static Utf8Span EncodeUtf8(List<nint> allocations, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return default;

        int byteCount = Encoding.UTF8.GetByteCount(value);
        byte* ptr = (byte*)NativeMemory.Alloc((nuint)byteCount);
        allocations.Add((nint)ptr);
        Encoding.UTF8.GetBytes(value, new Span<byte>(ptr, byteCount));
        return new Utf8Span { Ptr = ptr, Length = byteCount };
    }

    private static ByteSpan EncodeBytes(List<nint> allocations, byte[]? value)
    {
        if (value == null || value.Length == 0)
            return default;

        byte* ptr = (byte*)NativeMemory.Alloc((nuint)value.Length);
        allocations.Add((nint)ptr);
        value.AsSpan().CopyTo(new Span<byte>(ptr, value.Length));
        return new ByteSpan { Ptr = ptr, Length = value.Length };
    }

    private static NativeAttribute* EncodeAttributes(
        List<nint> allocations,
        TemplateSnapshot? template,
        OTelAttribute[]? attributes,
        out int attributeCount)
    {
        int templateCount = template?.Attributes.Count ?? 0;
        int inlineCount = attributes?.Length ?? 0;
        attributeCount = templateCount + inlineCount;
        if (attributeCount == 0)
            return null;

        var nativeAttributes = AllocArray<NativeAttribute>(allocations, attributeCount);
        int index = 0;

        if (template != null)
        {
            foreach (var kv in template.Attributes)
                nativeAttributes[index++] = EncodeAttribute(allocations, kv.Key, kv.Value);
        }

        if (attributes != null)
        {
            foreach (var attribute in attributes)
                nativeAttributes[index++] = EncodeAttribute(allocations, attribute.Key, attribute.Value);
        }

        return nativeAttributes;
    }

    private static NativeAttribute EncodeAttribute(List<nint> allocations, string key, object? value)
    {
        var attribute = new NativeAttribute
        {
            Key = EncodeUtf8(allocations, key)
        };

        switch (value)
        {
            case bool boolean:
                attribute.Kind = NativeAttributeValueKind.Bool;
                attribute.BoolValue = boolean ? 1 : 0;
                break;
            case int integer:
                attribute.Kind = NativeAttributeValueKind.Int64;
                attribute.Int64Value = integer;
                break;
            case long int64:
                attribute.Kind = NativeAttributeValueKind.Int64;
                attribute.Int64Value = int64;
                break;
            case double @double:
                attribute.Kind = NativeAttributeValueKind.Double;
                attribute.DoubleValue = @double;
                break;
            case float single:
                attribute.Kind = NativeAttributeValueKind.Double;
                attribute.DoubleValue = single;
                break;
            default:
                attribute.Kind = NativeAttributeValueKind.String;
                attribute.StringValue = EncodeUtf8(allocations, value?.ToString() ?? "");
                break;
        }

        return attribute;
    }
}

internal unsafe sealed class NativeBatchScope<T>(T* pointer, int count, List<nint> allocations) : IDisposable
    where T : unmanaged
{
    private readonly List<nint> _allocations = allocations;

    public T* Pointer { get; } = pointer;
    public int Count { get; } = count;

    public void Dispose()
    {
        for (int i = _allocations.Count - 1; i >= 0; i--)
            NativeMemory.Free((void*)_allocations[i]);
        _allocations.Clear();
    }
}
