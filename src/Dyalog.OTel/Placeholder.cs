using Dyalog.DWA;

[assembly: DwaVendor("DyalogOTel")]

namespace Dyalog.OTel;

/// <summary>
/// Placeholder to verify the project scaffolding builds.
/// Will be replaced by actual exports as implementation progresses.
/// </summary>
public static class Placeholder
{
    [DwaExport("pp_otel_version")]
    public static void Version(Localp rslt)
    {
        rslt.SetString("0.1.0-dev");
    }
}
