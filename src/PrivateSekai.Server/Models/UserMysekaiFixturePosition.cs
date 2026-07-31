using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiFixturePosition
{
    [Key("x")] public int x;
    [Key("y")] public int y;
    [Key("z")] public int z;
}
