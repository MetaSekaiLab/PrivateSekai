using MessagePack;

namespace PrivateSekai.Models;

/// <summary>
/// 对应 Sekai.UserPresentHistoryData
/// </summary>
[MessagePackObject]
public class UserPresentHistoryData
{
    [Key("presentId")] public string? presentId;
    [Key("seq")] public long seq;
    [Key("resourceType")] public string? resourceType;
    [Key("resourceId")] public int resourceId;
    [Key("resourceLevel")] public int resourceLevel;
    [Key("resourceQuantity")] public int resourceQuantity;
    [Key("expiredAt")] public long expiredAt;
    [Key("receivedAt")] public long receivedAt;
    [Key("reason")] public string? reason;
}
