using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPenlight
{
    [Key("penlightId")] public int penlightId;
    [Key("favoriteFlg")] public bool favoriteFlg;
}
