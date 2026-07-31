using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCheerfulCarnivalResultReward
{
    [Key("cheerfulCarnivalResultRewardId")] public int cheerfulCarnivalResultRewardId;
}
