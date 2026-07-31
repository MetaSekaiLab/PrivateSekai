using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserColorfulPassV2
{
    [Key("colorfulPassId")] public int colorfulPassId;
    [Key("continuousBuyHighTierCount")] public int continuousBuyHighTierCount;
    [Key("expiredAt")] public ulong expiredAt;
    [Key("continuousBuyHighTierExpiredAt")] public ulong continuousBuyHighTierExpiredAt;
}
