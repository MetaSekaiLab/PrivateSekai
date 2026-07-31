using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBoostItem
{
    [Key("userId")] public long userId;
    [Key("boostItemId")] public int boostItemId;
    [Key("quantity")] public int quantity;
}
