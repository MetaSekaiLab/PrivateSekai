using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCard
{
    [Key("userId")] public long userId;
    [Key("cardId")] public int cardId;
    [Key("level")] public int level;
    [Key("exp")] public int exp;
    [Key("totalExp")] public int totalExp;
    [Key("skillLevel")] public int skillLevel;
    [Key("skillExp")] public int skillExp;
    [Key("totalSkillExp")] public int totalSkillExp;
    [Key("masterRank")] public int masterRank;
    [Key("specialTrainingStatus")] public string? specialTrainingStatus;
    [Key("defaultImage")] public string? defaultImage;
    [Key("duplicateCount")] public int duplicateCount;
    [Key("createdAt")] public long createdAt;
    [Key("episodes")] public UserCardEpisode[]? episodes;
}
