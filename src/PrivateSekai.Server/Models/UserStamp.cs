using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserStamp
{
    [Key("userId")] public long userId;
    [Key("stampId")] public int stampId;
    [Key("obtainedAt")] public ulong obtainedAt;
}
