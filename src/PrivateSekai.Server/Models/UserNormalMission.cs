using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserNormalMission
{
    [Key("userId")] public long userId;
    [Key("normalMissionType")] public string? normalMissionType;
    [Key("progress")] public int progress;
    [Key("achievedMissionIds")] public int[]? achievedMissionIds;
}
