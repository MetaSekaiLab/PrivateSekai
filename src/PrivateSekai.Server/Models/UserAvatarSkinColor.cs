using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAvatarSkinColor
{
    [Key("avatarSkinColorId")] public int avatarSkinColorId;
}
