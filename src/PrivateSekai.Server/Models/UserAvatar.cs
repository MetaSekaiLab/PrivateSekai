using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAvatar
{
    [Key("avatarCostumeId")] public int? avatarCostumeId;
    [Key("avatarAccessoryId")] public int? avatarAccessoryId;
    [Key("avatarSkinColorId")] public int? avatarSkinColorId;
    [Key("avatarCoordinateId")] public int? avatarCoordinateId;
}
