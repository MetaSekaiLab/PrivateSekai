using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserGachaFreeResource
{
    [Key("userId")] public long userId;
    [Key("gachaBehaviorType")] public string? gachaBehaviorType;
    [Key("lastSpinAt")] public ulong lastSpinAt;
}
