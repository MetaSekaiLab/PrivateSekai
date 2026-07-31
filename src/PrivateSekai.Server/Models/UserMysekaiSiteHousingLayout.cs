using System.Collections.Generic;
using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiSiteHousingLayout
{
    [Key("mysekaiSiteId")] public int mysekaiSiteId;
    [Key("mysekaiSiteHousingLayouts")] public List<UserMysekaiSiteHousingLayoutData>? mysekaiSiteHousingLayouts;
    [Key("mysekaiFixtureSurfaceAppearances")] public List<MysekaiFixtureSurfaceAppearanceData>? mysekaiFixtureSurfaceAppearances;
    [Key("mysekaiPhenomenaId")] public int mysekaiPhenomenaId;
}
