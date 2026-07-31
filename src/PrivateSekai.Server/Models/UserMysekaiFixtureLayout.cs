using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiFixtureLayout
{
    [Key("position")] public UserMysekaiFixturePosition? position;
    [Key("rotation")] public int rotation;
    [Key("mysekaiFixtureId")] public int mysekaiFixtureId;
    [Key("textureId")] public int textureId;
    [Key("mysekaiUniqueId")] public string? mysekaiUniqueId;
}
