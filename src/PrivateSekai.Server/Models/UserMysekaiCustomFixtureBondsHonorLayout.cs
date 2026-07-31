using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiCustomFixtureBondsHonorLayout
{
    [Key("position")] public UserMysekaiFixturePosition? position;
    [Key("rotation")] public int rotation;
    [Key("mysekaiCustomFixtureId")] public int mysekaiCustomFixtureId;
    [Key("mysekaiUniqueId")] public string? mysekaiUniqueId;
    [Key("ornamentRotation")] public int ornamentRotation;
    [Key("bondsHonorId")] public int bondsHonorId;
    [Key("isFullSize")] public bool isFullSize;
    [Key("bondsHonorWordId")] public int bondsHonorWordId;
    [Key("isInverse")] public bool isInverse;
    [Key("isUnitVirtualSinger")] public bool isUnitVirtualSinger;
}
