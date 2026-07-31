using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserFixCostume
{
    [Key("characterId")] public int characterId;
    [Key("unit")] public string? unit;
}
