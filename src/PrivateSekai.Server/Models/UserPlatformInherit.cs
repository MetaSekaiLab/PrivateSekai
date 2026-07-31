using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPlatformInherit
{
    [Key("userId")] public long userId;
}
