using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserShop
{
    [Key("shopId")] public int shopId;
    [Key("userShopItems")] public UserShopItem[]? userShopItems;
}
