using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPanelMission
{
    [Key("panelMissionId")] public int panelMissionId;
    [Key("progress")] public int progress;
    [Key("userPanelMissionAchievedElements")] public UserPanelMissionAchievedElement[]? userPanelMissionAchievedElements;
    [Key("isRecievedReward")] public bool isRecievedReward;
}
