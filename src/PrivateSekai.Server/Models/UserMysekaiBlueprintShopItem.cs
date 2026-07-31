using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiBlueprintShopItem
{
    [Key("mysekaiBlueprintId")] public int mysekaiBlueprintId;
    [Key("seq")] public int seq;
    [Key("mysekaiBlueprintShopItemLotteryType")] public string? mysekaiBlueprintShopItemLotteryType;
    [Key("isBought")] public bool isBought;
}
