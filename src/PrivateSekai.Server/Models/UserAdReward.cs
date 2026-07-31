using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAdReward
{
    [Key("id")] public int id;
    [Key("lastPlayStartAt")] public ulong lastPlayStartAt;
    [Key("lastRewardedAt")] public ulong lastRewardedAt;
    [Key("dailyCount")] public int dailyCount;
}
