using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class ObjectData
{
    [Key("position")] public Float3? position;
    [Key("scale")] public Float3? scale;
    [Key("rotation")] public Float4? rotation;
    [Key("layer")] public int layer;
    [Key("lock")] public bool isLock;
    [Key("visible")] public bool visible;
}
