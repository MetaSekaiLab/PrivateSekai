using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMusicAchievement
{
    [Key("musicAchievementId")] public int musicAchievementId;
    [Key("musicId")] public int musicId;
}
