using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserGachaCeilExchangeSubstituteCost
{
    [Key("gachaCeilExchangeId")] public int gachaCeilExchangeId;
    [Key("substituteCostUsedCount")] public int substituteCostUsedCount;
}
