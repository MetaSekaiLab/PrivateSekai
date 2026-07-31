using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class ImageData
{
    [Key("objectData")] public ObjectData? objectData;
    [Key("id")] public int id;
}
