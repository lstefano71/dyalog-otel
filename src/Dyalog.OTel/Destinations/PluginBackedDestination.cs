using System.Collections.Concurrent;
using System.Text;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Config;
using Dyalog.OTel.PluginAbi;

namespace Dyalog.OTel.Destinations;

internal sealed unsafe class PluginBackedDestination : IDestination, IResourceAwareDestination, IEmitterAwareDestination
{
    private readonly LoadedDestinationFamily _family;
    private readonly nint _handle;
    private readonly string _name;
    private bool _destroyed;

    private PluginBackedDestination(LoadedDestinationFamily family, nint handle, string name)
    {
        _family = family;
        _handle = handle;
        _name = name;
    }

    public string Name => _name;

    public static PluginBackedDestination Create(LoadedDestinationFamily family, DestinationConfig config)
    {
        byte[] typeBytes = Encoding.UTF8.GetBytes(config.Type);
        byte[] configBytes = PropertyBagCodec.Encode(EnumerateConfig(config));
        nint handle = 0;
        int errorLength = 0;
        Span<byte> errorBuffer = stackalloc byte[2048];

        fixed (byte* typePtr = typeBytes)
        fixed (byte* configPtr = configBytes)
        fixed (byte* errorPtr = errorBuffer)
        {
            var status = family.Api->CreateDestination(
                typePtr,
                typeBytes.Length,
                configPtr,
                configBytes.Length,
                &handle,
                errorPtr,
                errorBuffer.Length,
                &errorLength);

            if (status != DestinationPluginStatus.Ok)
                throw new InvalidOperationException(
                    BuildFailureMessage($"create destination '{config.Type}'", family, status, errorBuffer, errorLength));
        }

        return new PluginBackedDestination(family, handle, config.Type.ToLowerInvariant());
    }

    public void Init()
    {
        EnsureNotDestroyed();
        var status = _family.Api->Init(_handle);
        if (status != DestinationPluginStatus.Ok)
        {
            Shutdown();
            throw new InvalidOperationException(BuildFailureMessage($"initialize destination '{_name}'", _family, status));
        }
    }

    public void SetResource(Dictionary<string, string> resource)
    {
        EnsureNotDestroyed();
        if (_family.Api->SetResource == null)
            return;

        byte[] blob = PropertyBagCodec.Encode(resource.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase));
        fixed (byte* blobPtr = blob)
        {
            var status = _family.Api->SetResource(_handle, blobPtr, blob.Length);
            if (status != DestinationPluginStatus.Ok)
            {
                Shutdown();
                throw new InvalidOperationException(BuildFailureMessage($"set resource on destination '{_name}'", _family, status));
            }
        }
    }

    public void SetEmitterConfig(string defaultEmitter, string defaultEmitterVersion, ConcurrentDictionary<string, string> registry)
    {
        EnsureNotDestroyed();
        if (_family.Api->SetEmitterConfig == null)
            return;

        // Encode as property bag: "default.name" → emitter, "default.version" → version, "<name>" → "<version>"
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("default.name", defaultEmitter),
            new("default.version", defaultEmitterVersion)
        };
        foreach (var (name, version) in registry)
            pairs.Add(new KeyValuePair<string, string>(name, version));

        byte[] blob = PropertyBagCodec.Encode(pairs);
        fixed (byte* blobPtr = blob)
        {
            var status = _family.Api->SetEmitterConfig(_handle, blobPtr, blob.Length);
            if (status != DestinationPluginStatus.Ok)
                throw new InvalidOperationException(BuildFailureMessage($"set emitter config on destination '{_name}'", _family, status));
        }
    }

    public void WriteLogs(ReadOnlySpan<LogRecord> batch)
    {
        EnsureNotDestroyed();
        using var nativeBatch = NativeBatchMarshaller.MarshalLogs(batch);
        var status = _family.Api->WriteLogs(_handle, nativeBatch.Pointer, nativeBatch.Count);
        if (status != DestinationPluginStatus.Ok)
            throw new InvalidOperationException(BuildFailureMessage($"write log batch to destination '{_name}'", _family, status));
    }

    public void WriteSpans(ReadOnlySpan<SpanRecord> batch)
    {
        EnsureNotDestroyed();
        using var nativeBatch = NativeBatchMarshaller.MarshalSpans(batch);
        var status = _family.Api->WriteSpans(_handle, nativeBatch.Pointer, nativeBatch.Count);
        if (status != DestinationPluginStatus.Ok)
            throw new InvalidOperationException(BuildFailureMessage($"write span batch to destination '{_name}'", _family, status));
    }

    public void WriteMetrics(ReadOnlySpan<MetricPoint> batch)
    {
        EnsureNotDestroyed();
        using var nativeBatch = NativeBatchMarshaller.MarshalMetrics(batch);
        var status = _family.Api->WriteMetrics(_handle, nativeBatch.Pointer, nativeBatch.Count);
        if (status != DestinationPluginStatus.Ok)
            throw new InvalidOperationException(BuildFailureMessage($"write metric batch to destination '{_name}'", _family, status));
    }

    public void Flush()
    {
        EnsureNotDestroyed();
        var status = _family.Api->Flush(_handle);
        if (status != DestinationPluginStatus.Ok)
            throw new InvalidOperationException(BuildFailureMessage($"flush destination '{_name}'", _family, status));
    }

    public void Shutdown()
    {
        if (_destroyed)
            return;

        try
        {
            var status = _family.Api->Shutdown(_handle);
            if (status != DestinationPluginStatus.Ok)
                throw new InvalidOperationException(BuildFailureMessage($"shutdown destination '{_name}'", _family, status));
        }
        finally
        {
            _family.Api->Destroy(_handle);
            _destroyed = true;
        }
    }

    public void Dispose() => Shutdown();

    private static IEnumerable<KeyValuePair<string, string>> EnumerateConfig(DestinationConfig config)
    {
        foreach (var kv in config.Properties.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            yield return kv;

        yield return new KeyValuePair<string, string>("signals",
            string.Join(",", config.Signals.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)));
    }

    private static string BuildFailureMessage(
        string operation,
        LoadedDestinationFamily family,
        DestinationPluginStatus status,
        ReadOnlySpan<byte> errorBuffer = default,
        int errorLength = 0)
    {
        string detail = errorLength > 0
            ? Encoding.UTF8.GetString(errorBuffer[..Math.Min(errorLength, errorBuffer.Length)])
            : status.ToString();

        return $"Failed to {operation} via companion DLL '{family.LibraryName}' at '{family.LibraryPath}': {detail}.";
    }

    private void EnsureNotDestroyed()
    {
        if (_destroyed)
            throw new ObjectDisposedException(nameof(PluginBackedDestination));
    }
}
