using System.Collections.Concurrent;

namespace Dyalog.OTel.Destinations;

internal sealed class EmitterConfigSnapshot(
    string defaultEmitter,
    string defaultEmitterVersion,
    ConcurrentDictionary<string, string>? registry)
{
    public string DefaultEmitter { get; } = defaultEmitter;
    public string DefaultEmitterVersion { get; } = defaultEmitterVersion;
    public ConcurrentDictionary<string, string>? Registry { get; } = registry;
}
