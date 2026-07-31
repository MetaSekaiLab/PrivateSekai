using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAvatarMotionFavorite
{
    [Key("avatarMotionId")] public int avatarMotionId;
    [Key("num")] public int num;
}
