using System.Runtime.InteropServices;
using System.Text;
using Dyalog.OTel.PluginAbi;

namespace Dyalog.OTel.Destinations;

public static unsafe class JsonlDestinationExports
{
    private static readonly nint ApiPointer = InitializeApiPointer();

    [UnmanagedCallersOnly(EntryPoint = DestinationPluginContract.ExportName)]
    public static DestinationFamilyApiV1* GetDestinationFamilyApi() => (DestinationFamilyApiV1*)ApiPointer;

    private static nint InitializeApiPointer()
    {
        var api = (DestinationFamilyApiV1*)NativeMemory.Alloc((nuint)1, (nuint)sizeof(DestinationFamilyApiV1));
        *api = new DestinationFamilyApiV1
        {
            AbiVersion = DestinationPluginContract.AbiVersion,
            CreateDestination = &CreateDestination,
            Init = &Init,
            SetResource = null,
            WriteLogs = &WriteLogs,
            WriteSpans = &WriteSpans,
            WriteMetrics = &WriteMetrics,
            WriteRawHistogramMetrics = null,
            Flush = &Flush,
            Shutdown = &Shutdown,
            Destroy = &Destroy,
            SetEmitterConfig = null
        };
        return (nint)api;
    }

    [UnmanagedCallersOnly]
    private static DestinationPluginStatus CreateDestination(
        byte* destinationType,
        int destinationTypeLength,
        byte* configBlob,
        int configBlobLength,
        nint* destinationHandle,
        byte* errorBuffer,
        int errorBufferLength,
        int* errorBytesWritten)
    {
        try
        {
            string type = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(destinationType, destinationTypeLength));
            var properties = NativeTelemetryMaterializer.DecodePropertyBag(configBlob, configBlobLength);
            var signals = NativeTelemetryMaterializer.ExtractSignals(properties);

            IDestination destination = type.ToLowerInvariant() switch
            {
                "file" => CreateFile(properties, signals),
                "http" => CreateHttp(properties, signals),
                _ => throw new InvalidOperationException(
                    $"JSONL companion does not support destination type '{type}'.")
            };

            *destinationHandle = GCHandle.ToIntPtr(GCHandle.Alloc(destination));
            if (errorBytesWritten != null)
                *errorBytesWritten = 0;
            return DestinationPluginStatus.Ok;
        }
        catch (Exception ex)
        {
            return WriteError(errorBuffer, errorBufferLength, errorBytesWritten, DestinationPluginStatus.CreateFailed, ex.Message);
        }
    }

    [UnmanagedCallersOnly]
    private static DestinationPluginStatus Init(nint destinationHandle)
    {
        try
        {
            Resolve(destinationHandle).Init();
            return DestinationPluginStatus.Ok;
        }
        catch
        {
            return DestinationPluginStatus.InitFailed;
        }
    }

    [UnmanagedCallersOnly]
    private static DestinationPluginStatus WriteLogs(nint destinationHandle, NativeLogRecord* records, int count)
    {
        try
        {
            Resolve(destinationHandle).WriteLogs(NativeTelemetryMaterializer.MaterializeLogs(records, count));
            return DestinationPluginStatus.Ok;
        }
        catch
        {
            return DestinationPluginStatus.WriteFailed;
        }
    }

    [UnmanagedCallersOnly]
    private static DestinationPluginStatus WriteSpans(nint destinationHandle, NativeSpanRecord* records, int count)
    {
        try
        {
            Resolve(destinationHandle).WriteSpans(NativeTelemetryMaterializer.MaterializeSpans(records, count));
            return DestinationPluginStatus.Ok;
        }
        catch
        {
            return DestinationPluginStatus.WriteFailed;
        }
    }

    [UnmanagedCallersOnly]
    private static DestinationPluginStatus WriteMetrics(nint destinationHandle, NativeMetricPoint* records, int count)
    {
        try
        {
            Resolve(destinationHandle).WriteMetrics(NativeTelemetryMaterializer.MaterializeMetrics(records, count));
            return DestinationPluginStatus.Ok;
        }
        catch
        {
            return DestinationPluginStatus.WriteFailed;
        }
    }

    [UnmanagedCallersOnly]
    private static DestinationPluginStatus Flush(nint destinationHandle)
    {
        try
        {
            Resolve(destinationHandle).Flush();
            return DestinationPluginStatus.Ok;
        }
        catch
        {
            return DestinationPluginStatus.FlushFailed;
        }
    }

    [UnmanagedCallersOnly]
    private static DestinationPluginStatus Shutdown(nint destinationHandle)
    {
        try
        {
            Resolve(destinationHandle).Shutdown();
            return DestinationPluginStatus.Ok;
        }
        catch
        {
            return DestinationPluginStatus.ShutdownFailed;
        }
    }

    [UnmanagedCallersOnly]
    private static void Destroy(nint destinationHandle)
    {
        if (destinationHandle == 0)
            return;

        var handle = GCHandle.FromIntPtr(destinationHandle);
        if (handle.IsAllocated)
            handle.Free();
    }

    private static IDestination Resolve(nint destinationHandle)
    {
        var handle = GCHandle.FromIntPtr(destinationHandle);
        return (IDestination)(handle.Target ?? throw new InvalidOperationException("Destination handle is not valid."));
    }

    private static JsonlFileDestination CreateFile(Dictionary<string, string> properties, HashSet<string> signals)
    {
        string rawPath = properties.GetValueOrDefault("path", "otel.jsonl");
        string path = Path.GetFullPath(rawPath);
        string rotate = properties.GetValueOrDefault("rotate", "monthly");
        return new JsonlFileDestination(path, ParseRotation(rotate), signals);
    }

    private static JsonlHttpDestination CreateHttp(Dictionary<string, string> properties, HashSet<string> signals)
    {
        string url = properties.GetValueOrDefault("url", "http://localhost:8080");
        return new JsonlHttpDestination(url, signals, ParseHeaders(properties));
    }

    private static RotationPeriod ParseRotation(string rawValue) => rawValue.ToLowerInvariant() switch
    {
        "hourly" => RotationPeriod.Hourly,
        "daily" => RotationPeriod.Daily,
        "monthly" => RotationPeriod.Monthly,
        "none" => RotationPeriod.None,
        _ => RotationPeriod.Monthly
    };

    private static Dictionary<string, string>? ParseHeaders(Dictionary<string, string> properties)
    {
        if (!properties.TryGetValue("headers", out var rawHeaders) || string.IsNullOrWhiteSpace(rawHeaders))
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in rawHeaders.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            if (separator > 0)
                headers[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
        }

        return headers.Count > 0 ? headers : null;
    }

    private static DestinationPluginStatus WriteError(
        byte* errorBuffer,
        int errorBufferLength,
        int* errorBytesWritten,
        DestinationPluginStatus status,
        string message)
    {
        if (errorBytesWritten != null)
            *errorBytesWritten = 0;

        if (errorBuffer != null && errorBufferLength > 0)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            int bytesToCopy = Math.Min(bytes.Length, errorBufferLength);
            bytes.AsSpan(0, bytesToCopy).CopyTo(new Span<byte>(errorBuffer, errorBufferLength));
            if (errorBytesWritten != null)
                *errorBytesWritten = bytesToCopy;
        }

        return status;
    }
}
