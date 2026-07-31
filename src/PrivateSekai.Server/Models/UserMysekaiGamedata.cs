using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiGamedata
{
    [Key("mysekaiRank")] public int mysekaiRank;
    [Key("totalExp")] public int totalExp;
    [Key("userMysekaiPhotoSeq")] public int userMysekaiPhotoSeq;
    [Key("mysekaiConvertFixtureLevel")] public int mysekaiConvertFixtureLevel;
    [Key("mysekaiMaterialPossessionLevel")] public int mysekaiMaterialPossessionLevel;
    [Key("mysekaiFixturePossessionLevel")] public int mysekaiFixturePossessionLevel;
    [Key("refreshedAt")] public long refreshedAt;
    [Key("isMysekaiTutorialEnd")] public bool isMysekaiTutorialEnd;
}
