using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserStampFavorite
{
    [Key("userId")] public long userId;
    [Key("stampId")] public int stampId;
    [Key("tabNum")] public int TabNum;
    [Key("num")] public int num;
}
