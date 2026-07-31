using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiFixture
{
    [Key("mysekaiFixtureId")] public int mysekaiFixtureId;
    [Key("textureId")] public int textureId;
    [Key("quantity")] public int quantity;
    [Key("lastObtainedAt")] public long lastObtainedAt;
}
