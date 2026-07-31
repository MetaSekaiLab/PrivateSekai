using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPanelMissionAchievedElement
{
    [Key("panelMissionId")] public int panelMissionId;
    [Key("achievedId")] public long achievedId;
    [Key("progress")] public int progress;
}
