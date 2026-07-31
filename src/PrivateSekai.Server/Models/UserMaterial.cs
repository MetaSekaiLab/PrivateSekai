using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMaterial
{
    [Key("materialId")] public int materialId;
    [Key("quantity")] public int quantity;
}
