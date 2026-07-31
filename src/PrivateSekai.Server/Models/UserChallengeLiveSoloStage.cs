using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserChallengeLiveSoloStage
{
    [Key("characterId")] public int characterId;
    [Key("challengeLiveStageType")] public string? challengeLiveStageType;
    [Key("rank")] public int rank;
    [Key("challengeLiveStageId")] public int challengeLiveStageId;
    [Key("challengeLiveStageStatus")] public string? challengeLiveStageStatus;
    [Key("point")] public int point;
}
