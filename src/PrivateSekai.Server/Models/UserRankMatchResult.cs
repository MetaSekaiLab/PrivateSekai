using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserRankMatchResult
{
    [Key("liveId")] public string? liveId;
    [Key("liveStatus")] public string? liveStatus;
    [Key("rankMatchSeasonId")] public int rankMatchSeasonId;
}
