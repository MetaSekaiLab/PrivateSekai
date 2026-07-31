using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class CollectionData
{
    [Key("objectData")] public ObjectData? objectData;
    [Key("id")] public int id;
    [Key("targetId")] public int targetId;
}
