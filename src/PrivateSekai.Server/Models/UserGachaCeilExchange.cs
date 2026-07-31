using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserGachaCeilExchange
{
    [Key("userId")] public long userId;
    [Key("gachaCeilExchangeId")] public int gachaCeilExchangeId;
    [Key("exchangeStatus")] public string? exchangeStatus;
    [Key("exchangeRemaining")] public int exchangeRemaining;
}
