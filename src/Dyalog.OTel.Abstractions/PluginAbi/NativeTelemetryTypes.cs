using System.Runtime.InteropServices;

namespace Dyalog.OTel.PluginAbi;

public enum NativeAttributeValueKind : int
{
    String = 0,
    Int64 = 1,
    Double = 2,
    Bool = 3
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Utf8Span
{
    public byte* Ptr;
    public int Length;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ByteSpan
{
    public byte* Ptr;
    public int Length;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeAttribute
{
    public Utf8Span Key;
    public NativeAttributeValueKind Kind;
    public long Int64Value;
    public double DoubleValue;
    public int BoolValue;
    public Utf8Span StringValue;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeLogRecord
{
    public long TimestampUnixNano;
    public int SeverityNumber;
    public Utf8Span SeverityText;
    public Utf8Span Body;
    public ByteSpan TraceId;
    public ByteSpan SpanId;
    public NativeAttribute* Attributes;
    public int AttributeCount;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeSpanRecord
{
    public ByteSpan TraceId;
    public ByteSpan SpanId;
    public ByteSpan ParentSpanId;
    public Utf8Span Name;
    public long StartTimeUnixNano;
    public long EndTimeUnixNano;
    public int StatusCode;
    public NativeAttribute* Attributes;
    public int AttributeCount;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeMetricPoint
{
    public long TimestampUnixNano;
    public Utf8Span Name;
    public double Value;
    public int Type;
    public NativeAttribute* Attributes;
    public int AttributeCount;
}
