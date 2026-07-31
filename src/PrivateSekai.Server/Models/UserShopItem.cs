using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserShopItem
{
    public const string STATUS_SALE = "sale";
    public const string STATUS_FORBIDDEN = "forbidden";
    public const string STATUS_SOLD_OUT = "sold_out";

    [IgnoreMember]
    public bool IsSale => status == STATUS_SALE;

    [Key("shopItemId")] public int shopItemId;
    [Key("level")] public int level;
    [Key("status")] public string? status;
}
