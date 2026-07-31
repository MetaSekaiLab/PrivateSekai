using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAvatarAccessory
{
    [Key("avatarAccessoryId")] public int avatarAccessoryId;
}
