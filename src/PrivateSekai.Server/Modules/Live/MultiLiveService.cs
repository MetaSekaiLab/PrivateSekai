extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Models;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Live;

public sealed class MultiLiveService(UserSession user, LiveMasterQueries master, LiveService live, LivePointService livePoints,
    CardService cards, BoostService boosts, ResourceMasterQueries resourceMaster, ResourceService resources, MissionService missions,
    Random random)
{
    public void Prepare(string liveId, MultiLiveRequest all, UserMultiLiveRequest request)
    {
        if (request.privateRoomSettings?.liveRuleType != "normal"
            || request.customMusicScoreId != null || request.musicCategoryName != "image")
            throw new MultiLiveRequestException(501);
        if (master.GetNormalLiveBoost(request.boostCount) == null) throw new MultiLiveRequestException(400);
        if (request.boostCount > 0 && boosts.Preview().current < request.boostCount) throw new MultiLiveRequestException(409);
        var settings = request.privateRoomSettings;
        if (settings.scoreCalculateType != null || settings.musicSelectionType != null || settings.musicDifficultyTypes != null
            || settings.deckTotalPowerRangeLower.HasValue || settings.deckTotalPowerRangeUpper.HasValue)
            throw new MultiLiveRequestException(501);
        if (master.GetMasterMusicDifficulty(request.musicDifficultyId)?.musicId != request.musicId
            || !master.IsMusicVideoSelection(request.musicId, request.musicVocalId, request.musicCategoryName))
            throw new MultiLiveRequestException(404);
        if (master.IsLimitedMusicOutOfTerm(request.musicId, user.Now))
            throw new MultiLiveRequestException(400, "limited_time_music_out_of_term");
        if (master.IsAprilFoolVocal(request.musicVocalId) && !master.IsAprilFoolSeason(user.Now))
            throw new MultiLiveRequestException(409, "out_of_period_2022_april_fool");
        var deck = (user.Data.userDecks ?? []).SingleOrDefault(d => d.deckId == request.deckId);
        if (deck == null) throw new MultiLiveRequestException(404);
        var cards = new[] { deck.member1, deck.member2, deck.member3, deck.member4, deck.member5 };
        if (cards.Any(id => id <= 0 || !(user.Data.userCards ?? []).Any(c => c.cardId == id))
            || cards.Distinct().Count() != 5 || !cards.Contains(deck.leader))
            throw new MultiLiveRequestException(409);
        user.Private.MultiLiveSessions.Clear();
        user.Private.MultiLiveSessions.Add(liveId, new() { Master = all, Start = request, CreatedAt = user.Now });
        user.NormalizeEventBreakTime();
        user.MarkChanged(nameof(SuiteUser.userEventBreakTime));
    }

    public UserMultiLiveClearResponse Clear(string liveId, UserMultiLiveClearRequest request)
    {
        if (!user.Private.MultiLiveSessions.TryGetValue(liveId, out var session)) throw new MultiLiveRequestException(404);
        if (request.superFeverFlg || request.isMirrored || request.disconnectUserIds?.Count > 0
            || request.musicCategoryName != "image" || request.privateRoomSettings?.liveRuleType != "normal"
            || request.ingameCutinCharacterArchiveVoiceGroupIds?.Count > 0)
            throw new MultiLiveRequestException(501);
        var slots = new[] { request.score1, request.score2, request.score3, request.score4, request.score5 };
        var ids = new[] { session.Master.userId1, session.Master.userId2, session.Master.userId3, session.Master.userId4, session.Master.userId5 };
        for (var i = 0; i < slots.Length; i++)
        {
            var score = slots[i];
            if (ids[i] == 0)
            {
                if (score != null) throw new MultiLiveRequestException(400);
                continue;
            }
            if (score == null || score.userId != ids[i] || score.score < 0 || score.life < 0
                || score.perfectCount < 0 || score.greatCount < 0 || score.goodCount < 0 || score.badCount < 0 || score.missCount < 0
                || score.maxCombo < 0 || score.tapCount < 0
                || master.GetMasterMusicDifficulty(score.musicDifficultyId)?.musicId != session.Start.musicId)
                throw new MultiLiveRequestException(400);
        }
        var played = slots.Where(s => s != null).ToArray();
        var own = played.SingleOrDefault(s => s.userId == user.UserId) ?? throw new MultiLiveRequestException(400);
        if (own.musicDifficultyId != session.Start.musicDifficultyId || played.Sum(s => (long)s.score) != request.totalScore)
            throw new MultiLiveRequestException(400);
        var rank = master.GetMultiScoreRank(own.musicDifficultyId, own.score);
        var totalRank = master.GetMultiScoreRank(own.musicDifficultyId, request.totalScore, true);
        if (totalRank is not ("rank_d" or "rank_c") || rank != "rank_c" || played.Length != 2)
            throw new MultiLiveRequestException(501);
        var boost = master.GetNormalLiveBoost(session.Start.boostCount) ?? throw new MultiLiveRequestException(400);
        if (boost.expRate <= 0 || boost.rewardRate <= 0 || boost.livePointRate <= 0)
            throw new InvalidOperationException("Invalid multiplayer boost rates.");
        var rewards = BuildScoreRewards(totalRank);
        foreach (var reward in rewards) reward.quantity = checked(reward.quantity * boost.rewardRate);
        var addedExp = checked((totalRank == "rank_c" ? 200 : 20) * boost.expRate);
        var deck = user.Data.userDecks.Single(d => d.deckId == session.Start.deckId);
        var context = new UserLiveSessionData
        {
            MusicId = session.Start.musicId, MusicDifficultyId = own.musicDifficultyId, DeckId = session.Start.deckId
        };
        var clear = new UserLiveClearRequest
        {
            score = own.score, perfectCount = own.perfectCount, greatCount = own.greatCount, goodCount = own.goodCount,
            badCount = own.badCount, missCount = own.missCount, maxCombo = own.maxCombo, life = own.life
        };
        var fullCombo = own.goodCount == 0 && own.badCount == 0 && own.missCount == 0;
        var fullPerfect = fullCombo && own.greatCount == 0;
        var mvp = own.score == played.Max(s => s.score);
        if (boost.costBoost > 0 && boosts.Preview().current < boost.costBoost) throw new MultiLiveRequestException(409);
        user.Private.MultiLiveSessions.Remove(liveId);
        boosts.Normalize();
        boosts.Consume(boost.costBoost);
        user.MarkChanged(nameof(SuiteUser.userBoost));
        var experience = live.GainPlayerExperience(addedExp);
        var cardResults = new[] { deck.member1, deck.member2, deck.member3, deck.member4, deck.member5 }
            .Select((id, index) => new DeckCardUpdateExpResult { index = index + 1, expResult = cards.GainExperience(id, checked(addedExp * 3)) }).ToArray();
        live.UpdateUserMusicResult(context, clear, fullCombo, fullPerfect, "multi", mvp);
        var achievements = live.GrantUserMusicAchievements(context, clear, rank);
        var achievementRewards = live.BuildMusicAchievementRewards(achievements);
        resources.Grant(rewards);
        resources.Grant(achievementRewards);
        missions.RecordCoinGain(rewards.Concat(achievementRewards).Concat(experience.Rewards)
            .Where(r => r.resourceType == "coin").Sum(r => r.quantity));
        var points = livePoints.Build(boost, true);
        missions.UpdateLiveMissionProgress(points);
        missions.RecordLiveRecords(own.maxCombo, master.ResolveMusicPlayLevel(own.musicDifficultyId));
        missions.RecordLiveFinish(true);
        missions.RecordManualLiveClear();
        if (mvp) missions.RecordMultiLiveMvp();
        live.RecordDeckLiveClear(session.Start.deckId);
        var users = played.Select(s => new UserMultiLiveClearScoreResponse
        {
            userId = s.userId, musicDifficultyId = s.musicDifficultyId, score = s.score, perfectCount = s.perfectCount,
            greatCount = s.greatCount, goodCount = s.goodCount, badCount = s.badCount, missCount = s.missCount,
            maxCombo = s.maxCombo, fullComboFlg = s.goodCount == 0 && s.badCount == 0 && s.missCount == 0,
            fullPerfectFlg = s.greatCount == 0 && s.goodCount == 0 && s.badCount == 0 && s.missCount == 0,
            mvpFlg = s.score == played.Max(p => p.score) && (s.userId == user.UserId || !mvp)
        }).ToArray();
        return new()
        {
            scoreRank = rank, multiScoreRank = totalRank, totalScore = request.totalScore,
            user1 = users.ElementAtOrDefault(0), user2 = users.ElementAtOrDefault(1), user3 = users.ElementAtOrDefault(2),
            user4 = users.ElementAtOrDefault(3), user5 = users.ElementAtOrDefault(4), userExpResult = experience.Result,
            deckCardExpResults = cardResults, unitExpResults = [], userDeck = deck, userMusicAchievements = achievements,
            scoreRankRewards = rewards, superFeverRewards = [], playerRankRewards = experience.Rewards, limitedTermScoreRankRewards = [],
            activeUserIds = played.Select(s => s.userId).ToList(), bondsUpdateExpResults = [], userEventDeviceTransferRestrict = new(),
            boost = boost, userLivePoint = points
        };
    }

    private UserResource[] BuildScoreRewards(string rank)
    {
        if (rank == "rank_d")
        {
            // 本地对五种属性碎片均匀抽选；现有资料未确认官方权重。
            var materials = Enumerable.Range(1, 5).Select(id => master.GetMultiRewardBox("material", id, 2)).ToArray();
            return resourceMaster.BuildResourcesFromBoxes("score_rank_reward_detail",
                [master.GetMultiRewardBox("coin", 0, 20), materials[random.Next(materials.Length)]]);
        }
        // 本地均匀选择已验证的C档材料组合，包含额外服装材料；不代表官方候选全集或掉率。
        (int Id, int Quantity)[][] materialsByResult =
        [
            [(1, 2), (2, 1), (7, 1), (12, 1)],
            [(3, 2), (2, 1), (7, 1)],
            [(3, 2), (5, 1), (8, 1)],
            [(5, 2), (3, 1), (7, 1)],
            [(4, 2), (4, 1), (10, 1)],
            [(2, 2), (5, 1), (9, 1)],
            [(1, 2), (5, 1), (10, 1)],
            [(4, 2), (5, 1), (9, 1)],
            [(1, 2), (3, 1), (10, 1)],
            [(3, 2), (5, 1), (9, 1)],
            [(1, 2), (2, 1), (6, 1)],
            [(4, 2), (5, 1), (10, 1), (11, 2)],
            [(4, 2), (4, 1), (7, 1)]
        ];
        var boxes = new[] { master.GetMultiRewardBox("coin", 0, 200), master.GetMultiRewardBox("practice_ticket", 1, 2) }
            .Concat(materialsByResult[random.Next(materialsByResult.Length)].Select(m => master.GetMultiRewardBox("material", m.Id, m.Quantity)));
        return resourceMaster.BuildResourcesFromBoxes("score_rank_reward_detail", boxes);
    }
}

public sealed class MultiLiveRequestException(int status, string errorCode = "") : Exception("多人演出准备请求被拒绝。")
{
    public int Status { get; } = status;
    public string ErrorCode { get; } = errorCode;
}
