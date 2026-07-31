using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCustomProfileGacha
{
    [Key("customProfileGachaId")] public int customProfileGachaId;
    [Key("customProfileGachaBehaviorId")] public int customProfileGachaBehaviorId;
    [Key("count")] public int count;
    [Key("lastSpinAt")] public long lastSpinAt;
}
