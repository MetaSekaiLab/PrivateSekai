using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBoostReceived
{
    [Key("receivedCount")] public int receivedCount;
    [Key("resetAt")] public long resetAt;
}
