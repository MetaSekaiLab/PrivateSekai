using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserGachaBonusPoint
{
    [Key("userId")] public long userId;
    [Key("gachaId")] public int gachaId;
    [Key("gachaBonusPoint")] public float gachaBonusPoint;
    [Key("totalGachaBonusPoint")] public float totalGachaBonusPoint;
}
