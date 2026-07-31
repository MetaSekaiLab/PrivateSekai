using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserOneTimeBehavior
{
    [Key("userId")] public long userId;
    [Key("oneTimeBehaviorType")] public string? oneTimeBehaviorType;
}
