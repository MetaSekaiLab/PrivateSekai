using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserStampFavoriteTabs
{
    [Key("userId")] public long userId;
    [Key("tabNum")] public int TabNum;
    [Key("tabName")] public string? TabName;
}
