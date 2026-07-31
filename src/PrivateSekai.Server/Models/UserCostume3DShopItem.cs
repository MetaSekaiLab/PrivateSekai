using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCostume3DShopItem
{
    [Key("costume3dShopItemId")] public int costume3dShopItemId;
    [Key("status")] public string? status;
}
