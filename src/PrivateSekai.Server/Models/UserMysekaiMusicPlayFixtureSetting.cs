using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiMusicPlayFixtureSetting
{
    [Key("mysekaiSiteId")] public int mysekaiSiteId;
    [Key("mysekaiMusicRecordId")] public int mysekaiMusicRecordId;
    [Key("musicVocalId")] public int musicVocalId;
    [Key("isInstrumental")] public bool isInstrumental;
}
