using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiMaterialPossession
{
    [Key("quantity")] public int quantity;
}
