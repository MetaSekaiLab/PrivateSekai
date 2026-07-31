using System.Collections.Generic;
using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiHarvestMap
{
    [Key("mysekaiSiteId")] public int mysekaiSiteId;
    [Key("userMysekaiSiteHarvestFixtures")] public List<UserMysekaiSiteHarvestFixture>? userMysekaiSiteHarvestFixtures;
    [Key("userMysekaiSiteHarvestResourceDrops")] public List<UserMysekaiSiteHarvestResourceDrop>? userMysekaiSiteHarvestResourceDrops;
}
