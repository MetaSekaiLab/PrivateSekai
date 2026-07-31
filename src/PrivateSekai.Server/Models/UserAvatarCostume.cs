using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAvatarCostume
{
    [Key("avatarCostumeId")] public int avatarCostumeId;
}
