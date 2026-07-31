using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserEventExchange
{
    public const string STATUS_EXCHANGEABLE = "exchangeable";
    public const string STATUS_OUT_PERIOD = "out_period";
    public const string STATUS_NOT_EXCHANGEABLE = "not_exchangeable";

    [Key("eventId")] public int eventId;
    [Key("eventExchangeId")] public int eventExchangeId;
    [Key("exchangeRemaining")] public int exchangeRemaining;
    [Key("exchangeStatus")] public string? exchangeStatus;
}
