using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserFriendInvitationCampaign
{
    [Key("id")] public int id;
    [Key("missionType")] public string? missionType;
    [Key("invitationCode")] public string? invitationCode;
    [Key("missions")] public UserFriendInvitationMission[]? missions;
}
