using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiSystemFixtureAction
{
    [Key("userId")] public long userId;
    [Key("mysekaiSystemFixtureId")] public int mysekaiSystemFixtureId;
}
