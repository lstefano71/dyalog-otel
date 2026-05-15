using System.Diagnostics;

namespace Dyalog.OTel.Diagnostics;

internal static class ModulePathResolver
{
    private static readonly string ModuleFileName = $"{typeof(ModulePathResolver).Assembly.GetName().Name}.dll";

    public static string GetModuleDirectory()
    {
        string? modulePath = TryGetModulePath();
        if (string.IsNullOrEmpty(modulePath))
            return AppContext.BaseDirectory;

        string? directory = Path.GetDirectoryName(modulePath);
        return string.IsNullOrEmpty(directory) ? AppContext.BaseDirectory : directory;
    }

    private static string? TryGetModulePath()
    {
        using Process process = Process.GetCurrentProcess();
        foreach (ProcessModule module in process.Modules)
        {
            if (string.Equals(module.ModuleName, ModuleFileName, StringComparison.OrdinalIgnoreCase))
                return module.FileName;
        }

        return null;
    }
}
