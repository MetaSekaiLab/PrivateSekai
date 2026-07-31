using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBeginnerMission
{
    [Key("userId")] public long userId;
    [Key("beginnerMissionType")] public string? beginnerMissionType;
    [Key("progress")] public int progress;
    [Key("achievedMissionIds")] public int[]? achievedMissionIds;
}
