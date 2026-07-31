using System.Collections.Generic;
using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiSiteHousingPresetSlot
{
    [Key("mysekaiSiteHousingPresetGroupId")] public int mysekaiSiteHousingPresetGroupId;
    [Key("slotNo")] public int slotNo;
    [Key("name")] public string? name;
    [Key("mysekaiSiteHousingLayouts")] public List<UserMysekaiSiteHousingLayoutData>? mysekaiSiteHousingLayouts;
    [Key("mysekaiFixtureSurfaceAppearances")] public List<MysekaiFixtureSurfaceAppearanceData>? mysekaiFixtureSurfaceAppearances;
    [Key("mysekaiPhenomenaId")] public int mysekaiPhenomenaId;
    [Key("mysekaiGateSkinId")] public int mysekaiGateSkinId;
    [Key("thumbnailPath")] public string? thumbnailPath;
}
