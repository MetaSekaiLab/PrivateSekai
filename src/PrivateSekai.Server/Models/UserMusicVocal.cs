using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMusicVocal
{
    [Key("musicId")] public int musicId;
    [Key("musicVocalId")] public int musicVocalId;
}
