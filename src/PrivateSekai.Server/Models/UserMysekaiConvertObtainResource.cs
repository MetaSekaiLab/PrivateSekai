using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiConvertObtainResource
{
    [Key("slotId")] public int slotId;
    [Key("progress")] public int progress;
    [Key("seq")] public int seq;
    [Key("resourceType")] public string? resourceType;
    [Key("resourceId")] public int resourceId;
    [Key("quantity")] public int quantity;
}
