using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiColorfulPass
{
    [Key("mysekaiColorfulPassId")] public int mysekaiColorfulPassId;
    [Key("expiredAt")] public long expiredAt;
}
