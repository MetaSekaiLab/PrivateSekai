using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserUnprocessedOrder
{
    public const string STATUS_UNPROCESSED = "unprocessed";
    public const string STATUS_PROCESSED = "processed";

    [Key("orderId")] public string? orderId;
    [Key("productId")] public string? productId;
    [Key("unprocessedOrderStatus")] public string? unprocessedOrderStatus;
}
