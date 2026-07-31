using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class Float3
{
    [Key("x")] public float x;
    [Key("y")] public float y;
    [Key("z")] public float z;
}
