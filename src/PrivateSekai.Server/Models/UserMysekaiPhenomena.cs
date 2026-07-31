using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiPhenomena
{
    [Key("mysekaiPhenomenaId")] public int mysekaiPhenomenaId;
    [Key("obtainedAt")] public long obtainedAt;
}
