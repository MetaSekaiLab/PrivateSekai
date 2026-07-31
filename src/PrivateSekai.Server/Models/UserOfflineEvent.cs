using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserOfflineEvent
{
    [Key("offlineEventId")] public int offlineEventId;
    [Key("name")] public string? name;
    [Key("displayStartAt")] public long displayStartAt;
    [Key("entryEvaluationStartAt")] public long entryEvaluationStartAt;
    [Key("entryEvaluationEndAt")] public long entryEvaluationEndAt;
    [Key("entryStartAt")] public long entryStartAt;
    [Key("entryEndAt")] public long entryEndAt;
    [Key("conditionContinuousBuyHighTierCount")] public int conditionContinuousBuyHighTierCount;
    [Key("url")] public string? url;
    [Key("offlineEventEntryStatus")] public string? offlineEventEntryStatus;
}
