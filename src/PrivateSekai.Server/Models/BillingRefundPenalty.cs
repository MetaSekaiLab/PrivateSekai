using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class BillingRefundPenalty
{
    [Key("penaltyCount")] public int penaltyCount;
    [Key("penaltyEndAt")] public ulong penaltyEndAt;
}
