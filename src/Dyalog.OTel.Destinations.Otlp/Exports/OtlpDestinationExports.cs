using System.Runtime.InteropServices;
using System.Text;
using Dyalog.OTel.PluginAbi;

namespace Dyalog.OTel.Destinations;

public static unsafe class OtlpDestinationExports
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
            SetResource = &SetResource,
            WriteLogs = &WriteLogs,
            WriteSpans = &WriteSpans,
            WriteMetrics = &WriteMetrics,
            WriteRawHistogramMetrics = null,
            Flush = &Flush,
            Shutdown = &Shutdown,
            Destroy = &Destroy,
            SetEmitterConfig = &SetEmitterConfig
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
            if (!type.Equals("otlp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"OTLP companion does not support destination type '{type}'.");

            var properties = NativeTelemetryMaterializer.DecodePropertyBag(configBlob, configBlobLength);
            properties.Remove("signals");

            IDestination destination = CreateOtlp(properties);
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
    private static DestinationPluginStatus SetResource(nint destinationHandle, byte* resourceBlob, int resourceBlobLength)
    {
        try
        {
            var resource = NativeTelemetryMaterializer.DecodePropertyBag(resourceBlob, resourceBlobLength);
            switch (Resolve(destinationHandle))
            {
                case OtlpJsonDestination json:
                    json.SetResource(resource);
                    break;
                case OtlpProtobufDestination proto:
                    proto.SetResource(resource);
                    break;
            }

            return DestinationPluginStatus.Ok;
        }
        catch
        {
            return DestinationPluginStatus.SetResourceFailed;
        }
    }

    [UnmanagedCallersOnly]
    private static DestinationPluginStatus SetEmitterConfig(nint destinationHandle, byte* configBlob, int configBlobLength)
    {
        try
        {
            var props = NativeTelemetryMaterializer.DecodePropertyBag(configBlob, configBlobLength);
            string defaultEmitter = props.GetValueOrDefault("default.name", "dyalog-otel");
            string defaultVersion = props.GetValueOrDefault("default.version", "");
            props.Remove("default.name");
            props.Remove("default.version");

            var registry = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(props, StringComparer.Ordinal);

            switch (Resolve(destinationHandle))
            {
                case OtlpJsonDestination json:
                    json.SetEmitterConfig(defaultEmitter, defaultVersion, registry);
                    break;
                case OtlpProtobufDestination proto:
                    proto.SetEmitterConfig(defaultEmitter, defaultVersion, registry);
                    break;
            }

            return DestinationPluginStatus.Ok;
        }
        catch
        {
            return DestinationPluginStatus.SetResourceFailed;
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

    private static IDestination CreateOtlp(Dictionary<string, string> properties)
    {
        string endpoint = properties.GetValueOrDefault("endpoint", "http://localhost:4318");
        Dictionary<string, string>? headers = ParseHeaders(properties);

        string protocol = properties.GetValueOrDefault("protocol", "");
        if (string.IsNullOrWhiteSpace(protocol))
            protocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL") ?? "";

        return protocol.ToLowerInvariant() switch
        {
            "json" or "http/json" => new OtlpJsonDestination(endpoint, headers),
            "protobuf" or "grpc" or "http/protobuf" => new OtlpProtobufDestination(endpoint, headers),
            _ => new OtlpProtobufDestination(endpoint, headers)
        };
    }

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
