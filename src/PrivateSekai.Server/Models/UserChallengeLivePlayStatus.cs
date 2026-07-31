using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserChallengeLivePlayStatus
{
    [IgnoreMember]
    public LiveStatus LiveStatus =>
        liveStatus switch
        {
            "start" => Models.LiveStatus.start,
            _ => Models.LiveStatus.cleared
        };

    [Key("userChallengeLiveId")] public string? userChallengeLiveId;
    [Key("musicId")] public int musicId;
    [Key("musicDifficultyId")] public int musicDifficultyId;
    [Key("musicVoiceId")] public int musicVoiceId;
    [Key("characterId")] public int characterId;
    [Key("liveStatus")] public string? liveStatus;
    [Key("playCount")] public int playCount;
    [Key("playStartAt")] public long playStartAt;
    [Key("playEndAt")] public long playEndAt;
}
