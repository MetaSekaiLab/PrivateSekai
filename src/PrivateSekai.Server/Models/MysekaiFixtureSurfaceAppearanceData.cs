using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class MysekaiFixtureSurfaceAppearanceData
{
    [Key("mysekaiFixtureSurfaceAppearanceType")] public string? mysekaiFixtureSurfaceAppearanceType;
    [Key("mysekaiFixtureId")] public int mysekaiFixtureId;
    [Key("textureId")] public int textureId;
}
