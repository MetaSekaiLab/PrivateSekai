using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAvatarCoordinate
{
    [Key("avatarCoordinateId")] public int avatarCoordinateId;
}
