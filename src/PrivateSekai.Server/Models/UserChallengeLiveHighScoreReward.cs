using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserChallengeLiveHighScoreReward
{
    [Key("characterId")] public int characterId;
    [Key("challengeLiveHighScoreRewardId")] public int challengeLiveHighScoreRewardId;
    [Key("challengeLiveHighScoreStatus")] public string? challengeLiveHighScoreStatus;
}
