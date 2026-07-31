using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiCustomFixtureRecordJacketLayout
{
    [Key("position")] public UserMysekaiFixturePosition? position;
    [Key("rotation")] public int rotation;
    [Key("mysekaiCustomFixtureId")] public int mysekaiCustomFixtureId;
    [Key("mysekaiUniqueId")] public string? mysekaiUniqueId;
    [Key("mysekaiMusicRecordId")] public int mysekaiMusicRecordId;
    [Key("musicAssetVariantId")] public int? musicAssetVariantId;
}
