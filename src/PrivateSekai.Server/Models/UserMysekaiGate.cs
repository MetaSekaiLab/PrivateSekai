using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiGate
{
    [Key("mysekaiGateId")] public int mysekaiGateId;
    [Key("mysekaiGateSkinId")] public int mysekaiGateSkinId;
    [Key("mysekaiGateLevel")] public int mysekaiGateLevel;
    [Key("visitCount")] public int visitCount;
    [Key("lastRefreshedAt")] public long lastRefreshedAt;
    [Key("isSettingAtHomeSite")] public bool isSettingAtHomeSite;
}
