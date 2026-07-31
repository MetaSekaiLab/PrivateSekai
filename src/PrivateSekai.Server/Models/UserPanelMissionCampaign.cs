using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPanelMissionCampaign
{
    [Key("panelMissionCampaignId")] public int panelMissionCampaignId;
    [Key("userPanelMissionSheets")] public UserPanelMissionSheet[]? userPanelMissionSheets;
}
