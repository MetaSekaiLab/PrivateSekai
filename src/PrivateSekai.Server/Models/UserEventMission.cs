using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserEventMission
{
    [Key("eventId")] public int eventId;
    [Key("eventMissionId")] public int eventMissionId;
    [Key("progress")] public int progress;
    [Key("isNewAchieved")] public bool isNewAchieved;
}
