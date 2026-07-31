using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMaterialExchange
{
    [Key("userId")] public long userId;
    [Key("materialExchangeId")] public int materialExchangeId;
    [Key("exchangeCount")] public int exchangeCount;
    [Key("totalExchangeCount")] public int totalExchangeCount;
    [Key("lastExchangedAt")] public long lastExchangedAt;
    [Key("exchangeStatus")] public string? exchangeStatus;
    [Key("exchangeRemaining")] public int exchangeRemaining;
    [Key("refreshedAt")] public long refreshedAt;
}
