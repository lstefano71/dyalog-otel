namespace Dyalog.OTel.PluginAbi;

public enum DestinationPluginStatus : int
{
    Ok = 0,
    InvalidConfiguration = 1,
    UnsupportedDestinationType = 2,
    CreateFailed = 3,
    InitFailed = 4,
    SetResourceFailed = 5,
    WriteFailed = 6,
    FlushFailed = 7,
    ShutdownFailed = 8,
    SetEmitterConfigFailed = 9
}
