using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserLiveMission
{
    [Key("userId")] public long userId;
    [Key("liveMissionPeriodId")] public int liveMissionPeriodId;
    [Key("liveMissionStatus")] public string? liveMissionStatus;
    [Key("progress")] public int progress;
    [Key("paidProgress")] public int paidProgress;
    [Key("achievedMissionIds")] public int[]? achievedMissionIds;
}
