using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiSiteHarvestResourceDrop
{
    [Key("resourceType")] public string? resourceType;
    [Key("resourceId")] public int resourceId;
    [Key("positionX")] public int positionX;
    [Key("positionZ")] public int positionZ;
    [Key("hp")] public int hp;
    [Key("seq")] public int seq;
    [Key("mysekaiSiteHarvestResourceDropStatus")] public string? mysekaiSiteHarvestResourceDropStatus;
    [Key("quantity")] public int quantity;
    [Key("mysekaiSiteHarvestSpawnLimitedRelationGroupId")] public int mysekaiSiteHarvestSpawnLimitedRelationGroupId;
}
