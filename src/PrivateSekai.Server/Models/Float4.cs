using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class Float4
{
    [Key("x")] public float x;
    [Key("y")] public float y;
    [Key("z")] public float z;
    [Key("w")] public float w;
}
