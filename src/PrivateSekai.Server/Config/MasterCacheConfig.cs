namespace PrivateSekai.Config;

public sealed class MasterCacheConfig
{
    public int MaxLoadedTables { get; init; } = 24;
    public long MaxLoadedBytes { get; init; } = 134_217_728;
    public string[] PinTables { get; init; } =
    [
        "cards",
        "gachas",
        "resourceBoxes",
        "shopItems"
    ];
}
