extern alias game;

using System.Text.Json;
using System.Text.Json.Serialization;
using game::Sekai;
using game::Sekai.MultiLive;
using PrivateSekai.Protocol;

namespace PrivateSekai.Client.Realtime;

public sealed class MultiLivePerformance
{
    public int DurationMilliseconds { get; init; }
    public UserMultiLiveClearScoreRequest OwnerScore { get; init; } = null!;
    public UserMultiLiveClearScoreRequest GuestScore { get; init; } = null!;

    public static MultiLivePerformance Read(string path)
    {
        var value = JsonSerializer.Deserialize<MultiLivePerformance>(File.ReadAllText(path), new JsonSerializerOptions(DumpJson.Options)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("缺少演出成绩用例。");
        if (value.DurationMilliseconds <= 0 || value.OwnerScore == null || value.GuestScore == null)
            throw new InvalidDataException("成绩用例必须包含等待时长及双方成绩。");
        return value;
    }

    public static UserMultiLiveClearScoreRequest Score(UserMultiLiveClearScoreRequest source, long userId, int difficultyId)
    {
        if (source.userId != 0 && source.userId != userId || source.musicDifficultyId != 0 && source.musicDifficultyId != difficultyId)
            throw new InvalidDataException("成绩用例与本次账号或难度不一致。");
        var score = DumpSerializer.Deserialize<UserMultiLiveClearScoreRequest>(DumpSerializer.Serialize(source));
        score.userId = userId;
        score.musicDifficultyId = difficultyId;
        if (new[] { score.score, score.perfectCount, score.greatCount, score.goodCount, score.badCount,
            score.missCount, score.maxCombo, score.life, score.tapCount }.Any(v => v < 0))
            throw new InvalidDataException("成绩数值不能为负数。");
        return score;
    }

    public static game::CP.Realtime.DynamicPropertyPayload Ready(UserMultiLiveClearScoreRequest score) => MultiLiveFlow.Property(
        (10, SyncPropertyEncoding.Object(DumpSerializer.Serialize(score))),
        (4, SyncPropertyEncoding.Int32((int)PlayerStatus.WaitResult)));

    public static UserMultiLiveClearRequest Result(long[] order, IReadOnlyDictionary<long, UserMultiLiveClearScoreRequest> scores)
    {
        if (order.Length is < 2 or > 5 || order.Distinct().Count() != order.Length || !order.ToHashSet().SetEquals(scores.Keys)
            || scores.Any(pair => pair.Key != pair.Value.userId))
            throw new InvalidDataException("成绩必须覆盖本场全部成员且与真实玩家一致。");
        var slots = order.Select(id => scores[id]).Concat(new UserMultiLiveClearScoreRequest[5]).Take(5).ToArray();
        return new UserMultiLiveClearRequest
        {
            score1 = slots[0], score2 = slots[1], score3 = slots[2], score4 = slots[3], score5 = slots[4],
            totalScore = checked(scores.Values.Sum(s => s.score)), superFeverFlg = false, disconnectUserIds = [],
            musicCategoryName = MusicCategory.image.ToString(), isMirrored = false,
            privateRoomSettings = new() { liveRuleType = "normal" }
        };
    }
}
