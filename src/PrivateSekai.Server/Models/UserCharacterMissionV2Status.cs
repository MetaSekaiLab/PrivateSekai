using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCharacterMissionV2Status
{
    [Key("userId")] public long userId;
    [Key("missionId")] public int missionId;
    [Key("parameterGroupId")] public int parameterGroupId;
    [Key("seq")] public int seq;
    [Key("characterId")] public int characterId;
    [Key("missionStatus")] public string? missionStatus;
}
