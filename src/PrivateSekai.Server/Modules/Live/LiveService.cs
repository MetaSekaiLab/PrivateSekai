extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Models;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Live;

public sealed class LiveService(
    UserSession user,
    LiveMasterQueries master,
    MissionMasterQueries missionMaster,
    MissionService missions,
    ResourceMasterQueries resourceMaster,
    ResourceService resources)
{
    public UserLive StartUserLive(UserLiveRequest request)
    {
        var userLiveId = Guid.NewGuid().ToString();
        user.Private.UserLiveSessions[userLiveId] = new UserLiveSessionData
        {
            UserLiveId = userLiveId,
            MusicId = request.musicId,
            MusicDifficultyId = request.musicDifficultyId,
            MusicVocalId = request.musicVocalId,
            DeckId = request.deckId,
            BoostCount = request.boostCount,
            IsAuto = request.isAuto,
            MusicCategoryName = request.musicCategoryName,
            CustomMusicScoreId = request.customMusicScoreId,
            CreatedAt = user.Now
        };

        user.NormalizeEventBreakTime();
        user.MarkChanged(nameof(SuiteUser.userEventBreakTime));
        return new UserLive
        {
            userLiveId = userLiveId,
            skills = BuildIngameLotterySkills(request.deckId),
            comboCutins = [],
            isInBreakTime = false
        };
    }

    public UserLiveClearResponse ClearUserLive(string userLiveId, UserLiveClearRequest request)
    {
        user.MarkChanged(nameof(SuiteUser.userEventBreakTime));
        user.Private.UserLiveSessions.Remove(userLiveId, out var session);

        var fullCombo = request.badCount == 0 && request.missCount == 0;
        var fullPerfect = request.greatCount == 0 &&
                          request.goodCount == 0 &&
                          request.badCount == 0 &&
                          request.missCount == 0;

        var scoreRank = session != null
            ? master.BuildScoreRank(session.MusicDifficultyId, request.score)
            : LiveMasterQueries.BuildScoreRank(request.score);
        var boost = master.BuildMasterBoost(session?.BoostCount ?? 0);
        var userLivePoint = BuildUserLivePoint(boost);
        var highScoreFlg = false;
        DeckCardUpdateExpResult[] deckCardExpResults = [];
        UserMusicAchievement[] grantedMusicAchievements = [];
        UserResource[] scoreRankRewards = [];
        UserResource[] musicAchievementRewards = [];
        if (session != null)
        {
            highScoreFlg = UpdateUserMusicResult(session, request, fullCombo, fullPerfect);
            deckCardExpResults = BuildDeckCardExpResults(session.DeckId);
            grantedMusicAchievements = GrantUserMusicAchievements(session, request, scoreRank);
            scoreRankRewards = BuildScoreRankRewards(session.MusicDifficultyId, scoreRank, boost);
            musicAchievementRewards = BuildMusicAchievementRewards(grantedMusicAchievements);
            ApplyLiveRewards(scoreRankRewards);
            ApplyLiveRewards(musicAchievementRewards);
            missions.UpdateLiveMissionProgress(userLivePoint);
            ConsumeBoost(session.BoostCount);
        }

        MergeLiveCharacterArchiveVoiceGroups(request.ingameCutinCharacterArchiveVoiceGroupIds);

        return new UserLiveClearResponse
        {
            score = request.score,
            perfectCount = request.perfectCount,
            greatCount = request.greatCount,
            goodCount = request.goodCount,
            badCount = request.badCount,
            missCount = request.missCount,
            maxCombo = request.maxCombo,
            highScoreFlg = highScoreFlg,
            fullComboFlg = fullCombo,
            fullPerfectFlg = fullPerfect,
            userExpResult = BuildNoopExpResult(),
            deckCardExpResults = deckCardExpResults,
            unitExpResults = [],
            userDeck = GetUserDeck(session?.DeckId),
            scoreRankRewards = scoreRankRewards,
            playerRankRewards = [],
            limitedTermScoreRankRewards = [],
            boost = boost,
            beforeEventPoint = 0,
            afterEventPoint = 0,
            beforeEventItemQuantity = 0,
            afterEventItemQuantity = 0,
            beforeWorldBloomChapterPoint = 0,
            afterWorldBloomChapterPoint = 0,
            worldBloomChapterNo = null,
            bondsUpdateExpResults = [],
            userEventDeviceTransferRestrict = new UserRestrictInfo(),
            userLivePoint = userLivePoint,
            isEventMaintenance = false,
            isInBreakTime = false
        };
    }

    public void ReceiveLiveCharacterArchiveVoiceResult(int liveResultCharacterArchiveVoiceGroupId)
    {
        if (liveResultCharacterArchiveVoiceGroupId <= 0)
            return;

        MergeLiveCharacterArchiveVoiceGroups([liveResultCharacterArchiveVoiceGroupId]);
    }

    private bool UpdateUserMusicResult(
        UserLiveSessionData session,
        UserLiveClearRequest request,
        bool fullCombo,
        bool fullPerfect)
    {
        user.Data.userMusicResults ??= [];
        var results = user.Data.userMusicResults.ToList();
        var difficultyType = master.ResolveMusicDifficultyType(session.MusicDifficultyId);
        var result = results.FirstOrDefault(r =>
            r.musicId == session.MusicId &&
            string.Equals(r.musicDifficultyType, difficultyType, StringComparison.Ordinal));

        var previousHighScore = result?.highScore ?? 0;
        var highScoreFlg = request.score > previousHighScore;

        if (result == null)
        {
            result = new UserMusicResult
            {
                musicId = session.MusicId,
                musicDifficultyType = difficultyType,
                playType = "solo",
                playResult = "clear"
            };
            results.Add(result);
        }

        result.playType = "solo";
        result.playResult = BuildPlayResult(fullCombo, fullPerfect, request.life);
        result.highScore = Math.Max(result.highScore, request.score);
        result.fullComboFlg = result.fullComboFlg || fullCombo;
        result.fullPerfectFlg = result.fullPerfectFlg || fullPerfect;

        user.Data.userMusicResults = results.ToArray();
        user.MarkChanged(nameof(SuiteUser.userMusicResults));
        return highScoreFlg;
    }

    private UserMusicAchievement[] GrantUserMusicAchievements(
        UserLiveSessionData session,
        UserLiveClearRequest request,
        string scoreRank)
    {
        var achievementIds = master.ResolveMusicAchievementIds(session.MusicDifficultyId, request.maxCombo, scoreRank);

        user.Data.userMusicAchievements ??= [];
        var achievements = user.Data.userMusicAchievements.ToList();
        var granted = new List<UserMusicAchievement>();

        foreach (var achievementId in achievementIds.Distinct())
        {
            if (achievements.Any(a => a.musicId == session.MusicId && a.musicAchievementId == achievementId))
                continue;

            var achievement = new UserMusicAchievement
            {
                musicId = session.MusicId,
                musicAchievementId = achievementId
            };
            achievements.Add(achievement);
            granted.Add(achievement);
        }

        if (granted.Count > 0)
        {
            user.Data.userMusicAchievements = achievements.ToArray();
            user.MarkChanged(nameof(SuiteUser.userMusicAchievements));
        }

        return granted.ToArray();
    }

    private UserResource[] BuildMusicAchievementRewards(UserMusicAchievement[] achievements)
    {
        var rewards = new List<UserResource>();
        foreach (var achievement in achievements)
        {
            var resourceBoxId = master.GetMusicAchievementResourceBoxId(achievement.musicAchievementId);
            rewards.AddRange(resourceMaster.BuildResourcesFromBox("music_achievement", resourceBoxId));
        }

        return rewards.ToArray();
    }

    private UserResource[] BuildScoreRankRewards(int musicDifficultyId, string scoreRank, MasterBoost boost)
    {
        var playLevel = master.ResolveMusicPlayLevel(musicDifficultyId);
        var resourceBoxIds = GetScoreRankRewardResourceBoxIds(playLevel, scoreRank);
        if (resourceBoxIds.Length == 0)
            return [];

        var rewardRate = Math.Max(1, boost.rewardRate);
        return resourceBoxIds
            .SelectMany(resourceBoxId => resourceMaster.BuildResourcesFromBox("score_rank_reward_detail", resourceBoxId))
            .Select(reward => new UserResource
            {
                resourceType = reward.resourceType,
                resourceId = reward.resourceId,
                resourceLevel = reward.resourceLevel,
                quantity = reward.quantity * rewardRate
            })
            .Where(reward => reward.quantity > 0)
            .ToArray();
    }

    private static int[] GetScoreRankRewardResourceBoxIds(int playLevel, string scoreRank) =>
        (playLevel, scoreRank) switch
        {
            // 6.5.5 capture 0054: musicDifficultyId 406, playLevel 6, rank_c.
            (6, "rank_c") => [62, 12, 19, 15, 47, 56],

            // 6.5.5 capture 0061: musicDifficultyId 313, playLevel 17, rank_d.
            (17, "rank_d") => [61, 20],

            _ => []
        };

    private void ConsumeBoost(int boostCount)
    {
        if (user.Data.userBoost == null || boostCount <= 0)
            return;

        user.Data.userBoost.current = Math.Max(0, user.Data.userBoost.current - boostCount);
        user.Data.userBoost.recoveryAt = (ulong)user.Now;
        user.MarkChanged(nameof(SuiteUser.userBoost));
    }

    private void MergeLiveCharacterArchiveVoiceGroups(IEnumerable<int>? groupIds)
    {
        if (groupIds == null)
            return;

        user.Data.userLiveCharacterArchiveVoice ??= new UserLiveCharacterArchiveVoice
        {
            characterArchiveVoiceGroupIds = []
        };
        user.Data.userLiveCharacterArchiveVoice.characterArchiveVoiceGroupIds ??= [];

        var changed = false;
        var current = user.Data.userLiveCharacterArchiveVoice.characterArchiveVoiceGroupIds;
        foreach (var groupId in groupIds.Where(id => id > 0))
        {
            if (current.Contains(groupId))
                continue;

            current.Add(groupId);
            changed = true;
        }

        if (changed)
            user.MarkChanged(nameof(SuiteUser.userLiveCharacterArchiveVoice));
    }

    private UserDeck? GetUserDeck(int? deckId)
    {
        if (deckId == null || user.Data.userDecks == null)
            return null;

        return user.Data.userDecks.FirstOrDefault(d => d.deckId == deckId.Value);
    }

    private IngameLotterySkill[] BuildIngameLotterySkills(int deckId)
    {
        var deck = GetUserDeck(deckId);
        if (deck == null)
            return [];

        var cardIds = new[] { deck.leader, deck.member1, deck.member2, deck.member3, deck.member4, deck.member5 };
        return cardIds
            .Where(cardId => cardId > 0)
            .Select((cardId, index) => new IngameLotterySkill
            {
                seq = index + 1,
                cardId = cardId,
                relationCardId = null,
                ingameCutinCharacterId = 0
            })
            .ToArray();
    }

    private DeckCardUpdateExpResult[] BuildDeckCardExpResults(int deckId)
    {
        var deck = GetUserDeck(deckId);
        if (deck == null || user.Data.userCards == null)
            return [];

        var cardIds = new[] { deck.member1, deck.member2, deck.member3, deck.member4, deck.member5 };
        return cardIds
            .Select((cardId, index) => new { cardId, index })
            .Where(slot => slot.cardId > 0 && user.Data.userCards.Any(card => card.cardId == slot.cardId))
            .Select(slot =>
            {
                var card = user.Data.userCards!.First(card => card.cardId == slot.cardId);
                return new DeckCardUpdateExpResult
                {
                    index = slot.index + 1,
                    expResult = new UpdateExpResult
                    {
                        beforeTotalExp = card.totalExp,
                        afterTotalExp = card.totalExp,
                        beforeExp = card.exp,
                        afterExp = card.exp,
                        beforeLevel = card.level,
                        afterLevel = card.level
                    }
                };
            })
            .ToArray();
    }

    private UpdateExpResult BuildNoopExpResult()
    {
        var gd = user.Data.userGamedata;
        return new UpdateExpResult
        {
            beforeTotalExp = gd?.totalExp ?? 0,
            afterTotalExp = gd?.totalExp ?? 0,
            beforeExp = gd?.exp ?? 0,
            afterExp = gd?.exp ?? 0,
            beforeLevel = gd?.rank ?? 0,
            afterLevel = gd?.rank ?? 0
        };
    }

    private UserLivePoint BuildUserLivePoint(MasterBoost boost) =>
        new()
        {
            addNormalProgress = boost.livePointRate,
            addDailyBonusProgress = 0,
            livePointBonusRemaining = boost.costBoost,
            liveMissionPeriodId = missionMaster.GetCurrentLiveMissionPeriodId()
        };

    private static string BuildPlayResult(bool fullCombo, bool fullPerfect, int life)
    {
        if (life <= 0)
            return "not_clear";
        if (fullPerfect)
            return "full_perfect";
        if (fullCombo)
            return "full_combo";
        return "clear";
    }

    private void ApplyLiveRewards(IEnumerable<UserResource> rewards)
    {
        foreach (var reward in rewards)
        {
            if (reward.quantity <= 0)
                continue;

            if ((reward.resourceType is "coin" or "virtual_coin") && user.Data.userGamedata == null)
                continue;

            if (reward.resourceType is "jewel" or "coin" or "virtual_coin"
                or "material" or "practice_ticket" or "costume_3d")
                resources.Grant(reward);
        }
    }
}
