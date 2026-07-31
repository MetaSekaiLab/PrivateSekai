using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBonds
{
    [Key("bondsGroupId")] public int bondsGroupId;
    [Key("rank")] public int rank;
    [Key("exp")] public int exp;
}
