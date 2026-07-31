using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiItem
{
    [Key("mysekaiItemId")] public int mysekaiItemId;
    [Key("quantity")] public int quantity;
    [Key("lastObtainedAt")] public long lastObtainedAt;
}
