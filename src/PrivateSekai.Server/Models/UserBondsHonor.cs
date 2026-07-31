using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBondsHonor
{
    [Key("bondsHonorId")] public int bondsHonorId;
    [Key("level")] public int level;
    [Key("obtainedAt")] public long obtainedAt;
    [Key("description")] public string? description;
}
