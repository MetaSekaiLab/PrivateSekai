using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMissionStatus
{
    [Key("userId")] public long userId;
    [Key("missionType")] public string? missionType;
    [Key("missionId")] public int missionId;
    [Key("missionStatus")] public string? missionStatus;
}
