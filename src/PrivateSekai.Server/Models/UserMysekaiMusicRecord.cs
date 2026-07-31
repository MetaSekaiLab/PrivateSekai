using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiMusicRecord
{
    [Key("mysekaiMusicRecordId")] public int mysekaiMusicRecordId;
    [Key("obtainedAt")] public long obtainedAt;
}
