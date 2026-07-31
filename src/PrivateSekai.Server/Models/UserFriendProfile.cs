using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserFriendProfile
{
    [Key("name")] public string? name;
    [Key("userProfile")] public UserProfile? userProfile;
    [Key("userProfileHonors")] public UserProfileHonor[]? userProfileHonors;
    [Key("userCard")] public UserCard? userCard;
    [Key("userHonorMissions")] public UserHonorMission[]? userHonorMissions;
    [Key("userPlayerFrames")] public UserPlayerFrame[]? userPlayerFrames;
    [Key("isMysekaiOwnerAcceptVisitForFriend")] public bool isMysekaiOwnerAcceptVisitForFriend;
}
