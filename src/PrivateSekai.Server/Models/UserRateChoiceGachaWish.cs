using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserRateChoiceGachaWish
{
    [Key("gachaId")] public int gachaId;
    [Key("gachaDetailId")] public int gachaDetailId;
    [Key("rateChoiceGachaWishId")] public int rateChoiceGachaWishId;
}
