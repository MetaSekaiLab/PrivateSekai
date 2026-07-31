using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserFriendInvitationCampaignMissionRewardCounts
{
    [Key("friendInvitationCampaignGroupId")] public int friendInvitationCampaignGroupId;
    [Key("countGroupId")] public int countGroupId;
    [Key("count")] public int count;
}
