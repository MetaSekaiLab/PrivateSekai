using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBlockData
{
    [Key("opponentUserId")] public long opponentUserId;
    [Key("blockStatus")] public string? blockStatus;
    [Key("opponentUserFriendProfile")] public UserFriendProfile? opponentUserFriendProfile;
}
