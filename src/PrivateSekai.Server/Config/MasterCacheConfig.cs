namespace PrivateSekai.Config;

public sealed class MasterCacheConfig
{
    public int MaxLoadedTables { get; init; } = 24;
    public long MaxLoadedBytes { get; init; } = 1_073_741_824;
    public string[] PinTables { get; init; } = [];
}
