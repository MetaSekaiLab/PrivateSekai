using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiCustomFixturePhotoLayout
{
    [Key("position")] public UserMysekaiFixturePosition? position;
    [Key("rotation")] public int rotation;
    [Key("mysekaiCustomFixtureId")] public int mysekaiCustomFixtureId;
    [Key("mysekaiUniqueId")] public string? mysekaiUniqueId;
    [Key("userMysekaiPhotoSeq")] public int userMysekaiPhotoSeq;
}
