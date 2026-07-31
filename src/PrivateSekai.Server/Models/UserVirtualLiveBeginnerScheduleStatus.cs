using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserVirtualLiveBeginnerScheduleStatus
{
    [Key("photonRoomName")] public string? photonRoomName;
    [Key("virtualLiveBeginnerScheduleId")] public int virtualLiveBeginnerScheduleId;
    [Key("virtualLiveScheduleStatus")] public string? virtualLiveScheduleStatus;
    [Key("cheerPoint")] public long cheerPoint;
    [Key("startAt")] public long startAt;
    [Key("endAt")] public long endAt;
}
