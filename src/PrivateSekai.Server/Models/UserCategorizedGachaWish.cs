using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCategorizedGachaWish
{
    [Key("gachaDetailId")] public int gachaDetailId;
    [Key("gachaId")] public int gachaId;
    [Key("categorizedWishType")] public string? categorizedWishType;
}
