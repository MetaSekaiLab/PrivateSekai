using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserHonor
{
    [Key("honorId")] public int honorId;
    [Key("level")] public int level;
    [Key("obtainedAt")] public long obtainedAt;
}
