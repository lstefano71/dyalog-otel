using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Dyalog.OTel.Config;
using Dyalog.OTel.Diagnostics;
using Dyalog.OTel.PluginAbi;

namespace Dyalog.OTel.Destinations;

internal static unsafe class DestinationFamilyLoader
{
    private static readonly ConcurrentDictionary<string, LoadedDestinationFamily> LoadedFamilies = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> FamilyLibraryByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["file"] = "Dyalog.OTel.Destinations.Jsonl.dll",
        ["http"] = "Dyalog.OTel.Destinations.Jsonl.dll",
        ["otlp"] = "Dyalog.OTel.Destinations.Otlp.dll"
    };

    public static IDestination Create(DestinationConfig config)
    {
        if (!FamilyLibraryByType.TryGetValue(config.Type, out var libraryName))
            throw new InvalidOperationException(
                $"Unknown destination type '{config.Type}'. Supported: file, http, otlp, text, console.");

        var family = LoadedFamilies.GetOrAdd(libraryName, LoadFamily);
        return PluginBackedDestination.Create(family, config);
    }

    private static LoadedDestinationFamily LoadFamily(string libraryName)
    {
        string libraryPath = Path.Combine(ModulePathResolver.GetModuleDirectory(), libraryName);
        if (!File.Exists(libraryPath))
            throw new InvalidOperationException(
                $"Configured destination companion DLL '{libraryName}' was not found beside the core DLL at '{libraryPath}'.");

        try
        {
            nint handle = NativeLibrary.Load(libraryPath);
            nint export = NativeLibrary.GetExport(handle, DestinationPluginContract.ExportName);
            var getApi = (delegate* unmanaged<DestinationFamilyApiV2*>)export;
            DestinationFamilyApiV2* api = getApi();
            if (api == null)
                throw new InvalidOperationException($"Destination companion DLL '{libraryName}' returned a null API table.");
            if (api->AbiVersion != DestinationPluginContract.AbiVersion)
                throw new InvalidOperationException(
                    $"Destination companion DLL '{libraryName}' uses ABI version {api->AbiVersion}, but the core expects {DestinationPluginContract.AbiVersion}.");

            return new LoadedDestinationFamily(libraryName, libraryPath, handle, api);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new InvalidOperationException(
                $"Failed to load destination companion DLL '{libraryName}' from '{libraryPath}': {ex.Message}", ex);
        }
    }
}

internal sealed unsafe class LoadedDestinationFamily(
    string libraryName,
    string libraryPath,
    nint libraryHandle,
    DestinationFamilyApiV2* api)
{
    public string LibraryName { get; } = libraryName;
    public string LibraryPath { get; } = libraryPath;
    public nint LibraryHandle { get; } = libraryHandle;
    public DestinationFamilyApiV2* Api { get; } = api;
}
