using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserHonorMission
{
    [Key("userId")] public long userId;
    [Key("honorMissionType")] public string? honorMissionType;
    [Key("progress")] public int progress;
    [Key("achievedMissionIds")] public int[]? achievedMissionIds;
}
