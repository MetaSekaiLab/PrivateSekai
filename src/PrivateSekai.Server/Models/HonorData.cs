using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class HonorData
{
    [Key("objectData")] public ObjectData? objectData;
    [Key("id")] public int id;
    [Key("fullSize")] public bool fullSize;
}
