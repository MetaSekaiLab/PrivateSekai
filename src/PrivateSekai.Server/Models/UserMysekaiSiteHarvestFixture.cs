using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiSiteHarvestFixture
{
    [Key("mysekaiSiteHarvestFixtureId")] public int mysekaiSiteHarvestFixtureId;
    [Key("positionX")] public int positionX;
    [Key("positionZ")] public int positionZ;
    [Key("hp")] public int hp;
    [Key("userMysekaiSiteHarvestFixtureStatus")] public string? userMysekaiSiteHarvestFixtureStatus;
    [Key("mysekaiSiteHarvestSpawnLimitedRelationGroupId")] public int mysekaiSiteHarvestSpawnLimitedRelationGroupId;
}
