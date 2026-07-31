using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiFixtureGameCharacterPerformanceBonus
{
    [Key("gameCharacterId")] public int gameCharacterId;
    [Key("totalBonusRate")] public int totalBonusRate;
}
