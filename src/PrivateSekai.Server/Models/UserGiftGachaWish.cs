using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserGiftGachaWish
{
    [Key("userId")] public int userId;
    [Key("gachaId")] public int gachaId;
    [Key("giftGachaExchangeId")] public int giftGachaExchangeId;
    [Key("createdAt")] public long createdAt;
    [Key("updatedAt")] public long updatedAt;
}
