using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiTreasureBox
{
    [Key("mysekaiSiteHarvestRefreshType")] public string? mysekaiSiteHarvestRefreshType;
    [Key("seq")] public int seq;
    [Key("transportSeconds")] public int transportSeconds;
    [Key("userMysekaiTreasureBoxStatus")] public string? userMysekaiTreasureBoxStatus;
    [Key("mysekaiSiteId")] public int mysekaiSiteId;
    [Key("positionX")] public int positionX;
    [Key("positionZ")] public int positionZ;
}
