using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserGachaCeilItem
{
    [Key("userId")] public long userId;
    [Key("gachaCeilItemId")] public int gachaCeilItemId;
    [Key("quantity")] public int quantity;
}
