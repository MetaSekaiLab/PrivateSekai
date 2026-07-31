using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiCustomFixturePenlightLayout
{
    [Key("position")] public UserMysekaiFixturePosition? position;
    [Key("rotation")] public int rotation;
    [Key("mysekaiCustomFixtureId")] public int mysekaiCustomFixtureId;
    [Key("mysekaiUniqueId")] public string? mysekaiUniqueId;
    [Key("ornamentRotation")] public int ornamentRotation;
    [Key("penlightId")] public int penlightId;
}
