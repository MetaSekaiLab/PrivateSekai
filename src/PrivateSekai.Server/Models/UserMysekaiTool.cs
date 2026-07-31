using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiTool
{
    [Key("mysekaiToolId")] public int mysekaiToolId;
    [Key("quantity")] public int quantity;
    [Key("durability")] public int durability;
    [Key("lastObtainedAt")] public long lastObtainedAt;
}
