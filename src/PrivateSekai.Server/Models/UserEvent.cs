using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserEvent
{
    [Key("eventId")] public int eventId;
    [Key("eventPoint")] public int eventPoint;
    [Key("rank")] public long rank;
    [Key("rankingRewardReceivedAt")] public long rankingRewardReceivedAt;
}
