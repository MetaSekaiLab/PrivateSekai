using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAvatarMotion
{
    [Key("avatarMotionId")] public int avatarMotionId;
}
