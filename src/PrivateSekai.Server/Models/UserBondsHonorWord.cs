using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBondsHonorWord
{
    [Key("bondsHonorWordId")] public int bondsHonorWordId;
    [Key("obtainedAt")] public long obtainedAt;
}
