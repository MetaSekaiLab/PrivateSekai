using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserFriendInvitationMission
{
    [Key("missionCategoryId")] public int missionCategoryId;
    [Key("count")] public int count;
}
