using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserVirtualShopItem
{
    public const string STATUS_SALE = "sale";
    public const string STATUS_SOLD_OUT = "sold_out";

    [Key("virtualShopId")] public int virtualShopId;
    [Key("virtualShopItemId")] public int virtualShopItemId;
    [Key("status")] public string? status;
    [Key("buyCount")] public int buyCount;
}
