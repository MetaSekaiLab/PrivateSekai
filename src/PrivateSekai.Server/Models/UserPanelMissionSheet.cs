using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPanelMissionSheet
{
    [Key("panelMissionCampaignId")] public int panelMissionCampaignId;
    [Key("panelMissionSheetId")] public int panelMissionSheetId;
}
