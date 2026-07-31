using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPreliminaryTournamentLiveResult
{
    [Key("preliminaryTournamentMusicId")] public int preliminaryTournamentMusicId;
    [Key("preliminaryTournamentId")] public int preliminaryTournamentId;
    [Key("score")] public int score;
    [Key("scoreRank")] public string? scoreRank;
    [Key("perfectCount")] public int perfectCount;
    [Key("greatCount")] public int greatCount;
    [Key("goodCount")] public int goodCount;
    [Key("badCount")] public int badCount;
    [Key("missCount")] public int missCount;
    [Key("comboCount")] public int comboCount;
    [Key("scoreUpdatedAt")] public long scoreUpdatedAt;
}
