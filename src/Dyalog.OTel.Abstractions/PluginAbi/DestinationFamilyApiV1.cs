using System.Runtime.InteropServices;

namespace Dyalog.OTel.PluginAbi;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct DestinationFamilyApiV1
{
    public nuint AbiVersion;
    public delegate* unmanaged<byte*, int, byte*, int, nint*, byte*, int, int*, DestinationPluginStatus> CreateDestination;
    public delegate* unmanaged<nint, DestinationPluginStatus> Init;
    public delegate* unmanaged<nint, byte*, int, DestinationPluginStatus> SetResource;
    public delegate* unmanaged<nint, NativeLogRecord*, int, DestinationPluginStatus> WriteLogs;
    public delegate* unmanaged<nint, NativeSpanRecord*, int, DestinationPluginStatus> WriteSpans;
    public delegate* unmanaged<nint, NativeMetricPoint*, int, DestinationPluginStatus> WriteMetrics;
    public delegate* unmanaged<nint, NativeMetricPoint*, int, DestinationPluginStatus> WriteRawHistogramMetrics;
    public delegate* unmanaged<nint, DestinationPluginStatus> Flush;
    public delegate* unmanaged<nint, DestinationPluginStatus> Shutdown;
    public delegate* unmanaged<nint, void> Destroy;
}
