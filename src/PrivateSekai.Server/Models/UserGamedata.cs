using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserGamedata
{
    [Key("userId")] public long userId;
    [Key("name")] public string? name;
    [Key("deck")] public int deck;
    [Key("rank")] public int rank;
    [Key("exp")] public int exp;
    [Key("totalExp")] public int totalExp;
    [Key("coin")] public int coin;
    [Key("virtualCoin")] public int virtualCoin;
    [Key("lastLoginAt")] public long lastLoginAt;
    [Key("customProfileId")] public int? customProfileId;
}
