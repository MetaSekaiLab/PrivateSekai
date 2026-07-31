using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiPhoto
{
    [Key("seq")] public int seq;
    [Key("mysekaiPhotoDecorationId")] public int mysekaiPhotoDecorationId;
    [Key("obtainedAt")] public long obtainedAt;
    [Key("imagePath")] public string? imagePath;
}
