using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserFriend
{
    [Key("opponentUserId")] public long opponentUserId;
    [Key("message")] public string? message;
    [Key("friendStatus")] public string? friendStatus;
    [Key("isFavorite")] public bool isFavorite;
    [Key("requestExpiredAt")] public long requestExpiredAt;
    [Key("approvedAt")] public long approvedAt;
    [Key("opponentUserFriendProfile")] public UserFriendProfile? opponentUserFriendProfile;
    [Key("userLoginStatus")] public UserLoginStatus? userLoginStatus;
}
