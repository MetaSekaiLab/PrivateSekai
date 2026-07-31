using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class BondsHonorData
{
    [Key("objectData")] public ObjectData? objectData;
    [Key("id")] public int id;
    [Key("fullSize")] public bool fullSize;
    [Key("wordId")] public int wordId;
    [Key("inverse")] public bool inverse;
    [Key("useUnitVirtualSinger")] public bool useUnitVirtualSinger;
}
