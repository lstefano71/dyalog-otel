using Dyalog.DWA;

namespace Dyalog.OTel.Resources;

/// <summary>
/// Auto-detects resource attributes from the environment and the APL interpreter.
/// Called once at pipeline start. User-provided attributes override auto-detected ones.
/// </summary>
public static class ResourceDetector
{
    public static Dictionary<string, string> Detect()
    {
        var attrs = new Dictionary<string, string>
        {
            ["telemetry.sdk.name"] = "dyalog-otel",
            ["telemetry.sdk.language"] = "apl",
            ["telemetry.sdk.version"] = "0.1.0",
            ["host.name"] = Environment.MachineName,
            ["process.pid"] = Environment.ProcessId.ToString()
        };

        // APL-specific attributes via DWA Workspace API
        try
        {
            string aplVersion = Workspace.GetAplVersionString();
            if (!string.IsNullOrEmpty(aplVersion))
                attrs["apl.version"] = aplVersion;
        }
        catch
        {
            // Workspace may not be initialized yet during testing
        }

        return attrs;
    }

    /// <summary>
    /// Merge auto-detected attributes with user-provided ones.
    /// User attributes take precedence.
    /// </summary>
    public static Dictionary<string, string> Merge(
        Dictionary<string, string> autoDetected,
        Dictionary<string, string> userProvided)
    {
        foreach (var kv in userProvided)
            autoDetected[kv.Key] = kv.Value;
        return autoDetected;
    }
}
