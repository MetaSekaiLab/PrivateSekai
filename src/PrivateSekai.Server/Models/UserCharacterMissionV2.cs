using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCharacterMissionV2
{
    [Key("userId")] public long userId;
    [Key("characterMissionType")] public string? characterMissionType;
    [Key("characterId")] public int characterId;
    [Key("progress")] public int progress;
    [Key("achievedMissions")] public UserCharacterMissionV2Status[]? achievedMissions;
}
