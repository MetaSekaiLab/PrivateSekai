using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCharacterCostume3D
{
    [Key("characterId")] public int characterId;
    [Key("unit")] public string? unit;
    [Key("headCostume3dId")] public int headCostume3dId;
    [Key("hairCostume3dId")] public int hairCostume3dId;
    [Key("bodyCostume3dId")] public int bodyCostume3dId;
}
