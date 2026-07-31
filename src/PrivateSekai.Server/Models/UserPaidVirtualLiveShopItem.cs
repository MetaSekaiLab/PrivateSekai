using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPaidVirtualLiveShopItem
{
    [Key("paidVirtualLiveShopItemIds")] public int[]? paidVirtualLiveShopItemIds;
}
