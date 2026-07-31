using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserRankMatchSeason
{
    [Key("rankMatchSeasonId")] public int rankMatchSeasonId;
    [Key("rankMatchTierId")] public int rankMatchTierId;
    [Key("tierPoint")] public int tierPoint;
    [Key("totalTierPoint")] public int totalTierPoint;
    [Key("rank")] public int rank;
    [Key("playCount")] public int playCount;
    [Key("consecutiveWinCount")] public int consecutiveWinCount;
    [Key("winCount")] public int winCount;
    [Key("loseCount")] public int loseCount;
    [Key("drawCount")] public int drawCount;
    [Key("penaltyCount")] public int penaltyCount;
    [Key("bonusPoint")] public int bonusPoint;
    [Key("maxConsecutiveWinCount")] public int maxConsecutiveWinCount;
    [Key("playableAt")] public long playableAt;
}
