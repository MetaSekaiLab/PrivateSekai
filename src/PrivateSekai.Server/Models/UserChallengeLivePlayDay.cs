using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserChallengeLivePlayDay
{
    public const string REWARD_STATUS_SELECT = "select";
    public const string REWARD_STATUS_RECEIVED = "received";

    [Key("playDays")] public int playDays;
    [Key("challengeLivePlayDayRewardStatus")] public string? challengeLivePlayDayRewardStatus;
    [Key("playDaysResetAt")] public long playDaysResetAt;
    [Key("lastPlayStartAt")] public long lastPlayStartAt;
}
