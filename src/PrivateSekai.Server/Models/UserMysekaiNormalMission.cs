using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiNormalMission
{
    [Key("mysekaiNormalMissionId")] public int mysekaiNormalMissionId;
    [Key("progress")] public int progress;
    [Key("mysekaiMissionStatus")] public string? mysekaiMissionStatus;
}
