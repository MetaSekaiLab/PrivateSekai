using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCharacterLiveUsageCount
{
    [Key("characterId")] public int characterId;
    [Key("characterLiveUsageType")] public string? characterLiveUsageType;
    [Key("usageCount")] public int usageCount;
}
