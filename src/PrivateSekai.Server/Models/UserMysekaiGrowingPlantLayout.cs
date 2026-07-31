using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiGrowingPlantLayout
{
    [Key("position")] public UserMysekaiFixturePosition? position;
    [Key("rotation")] public int rotation;
    [Key("mysekaiUniqueId")] public string? mysekaiUniqueId;
    [Key("mysekaiFixtureId")] public int mysekaiFixtureId;
}
