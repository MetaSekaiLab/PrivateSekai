using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserUnit
{
    [Key("userId")] public long userId;
    [Key("unit")] public string? unit;
    [Key("rank")] public int rank;
    [Key("exp")] public int exp;
    [Key("totalExp")] public int totalExp;
}
