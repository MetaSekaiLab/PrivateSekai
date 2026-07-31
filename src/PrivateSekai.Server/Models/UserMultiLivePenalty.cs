using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMultiLivePenalty
{
    [Key("penaltyEndAt")] public long penaltyEndAt;
}
