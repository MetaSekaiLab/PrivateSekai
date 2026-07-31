using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserVirtualShop
{
    [Key("virtualShopId")] public int virtualShopId;
    [Key("userVirtualShopItems")] public UserVirtualShopItem[]? userVirtualShopItems;
}
