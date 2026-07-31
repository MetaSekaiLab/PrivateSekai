using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserGacha
{
    [Key("userId")] public long userId;
    [Key("gachaId")] public int gachaId;
    [Key("gachaBehaviorId")] public int gachaBehaviorId;
    [Key("count")] public int count;
    [Key("lastSpinAt")] public long lastSpinAt;
}
