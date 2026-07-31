using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserEventBreakTime
{
    [Key("eventId")] public int eventId;
    [Key("worldBloomId")] public int? worldBloomId;
    [Key("currentPoint")] public int currentPoint;
    [Key("lastDecreaseAt")] public long lastDecreaseAt;
    [Key("lastIncreaseAt")] public long? lastIncreaseAt;
    [Key("playTimeUsedMillis")] public long playTimeUsedMillis;
    [Key("playTimeUsedUpdatedAt")] public long? playTimeUsedUpdatedAt;
}
