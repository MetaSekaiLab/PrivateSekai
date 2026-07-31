using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserReleaseCondition
{
    [Key("userId")] public long userId;
    [Key("releaseConditionId")] public int releaseConditionId;
    [Key("createdAt")] public long createdAt;
}
