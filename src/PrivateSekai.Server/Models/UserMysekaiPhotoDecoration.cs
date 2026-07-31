using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiPhotoDecoration
{
    [Key("mysekaiPhotoDecorationId")] public int mysekaiPhotoDecorationId;
    [Key("obtainedAt")] public long obtainedAt;
}
