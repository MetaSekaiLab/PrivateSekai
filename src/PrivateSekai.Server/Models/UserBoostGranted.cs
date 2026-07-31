using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBoostGranted
{
    [Key("boostPresentId")] public int boostPresentId;
    [Key("grantedCount")] public int grantedCount;
    [Key("resetAt")] public long resetAt;
}
