using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBillingRefund
{
    [Key("userId")] public long userId;
    [Key("billingShopItemId")] public int billingShopItemId;
    [Key("billingRefundStatus")] public string? billingRefundStatus;
}
