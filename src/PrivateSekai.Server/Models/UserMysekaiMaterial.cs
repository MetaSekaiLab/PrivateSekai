using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiMaterial
{
    [Key("mysekaiMaterialId")] public int mysekaiMaterialId;
    [Key("quantity")] public int quantity;
    [Key("lastObtainedAt")] public long lastObtainedAt;
}
