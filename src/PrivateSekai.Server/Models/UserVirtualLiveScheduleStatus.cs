using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserVirtualLiveScheduleStatus
{
    [Key("photonRoomName")] public string? photonRoomName;
    [Key("virtualLiveScheduleId")] public int virtualLiveScheduleId;
    [Key("virtualLiveScheduleStatus")] public string? virtualLiveScheduleStatus;
    [Key("cheerPoint")] public long cheerPoint;
    [Key("startAt")] public long startAt;
    [Key("endAt")] public long endAt;
}
