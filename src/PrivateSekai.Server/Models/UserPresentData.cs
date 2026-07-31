using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPresentData
{
    [Key("presentId")] public string? presentId;
    [Key("seq")] public long seq;
    [Key("resourceType")] public string? resourceType;
    [Key("resourceId")] public int resourceId;
    [Key("resourceLevel")] public int resourceLevel;
    [Key("resourceQuantity")] public int resourceQuantity;
    [Key("expiredAt")] public long expiredAt;
    [Key("reason")] public string? reason;
}
