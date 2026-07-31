using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBeginnerMissionV2
{
    [Key("beginnerMissionV2Id")] public int beginnerMissionV2Id;
    [Key("progress")] public int progress;
    [Key("isNewAchieved")] public bool isNewAchieved;
}
