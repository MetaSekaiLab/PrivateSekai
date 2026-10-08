extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Models;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Live;

public sealed class LiveService(
    UserSession user,
    LiveMasterQueries master,
    BoostService boosts,
    MissionMasterQueries missionMaster,
    MissionService missions,
    CardService cards,
    ResourceMasterQueries resourceMaster,
    ResourceService resources)
{
    public int RecoverBoost(UserBoostItemRequest request)
    {
        if (request.costs == null || request.costs.Length == 0 ||
            request.costs.Any(c => c == null || c.resourceType != "boost_item" || c.resourceId <= 0 || c.quantity <= 0))
            throw new ArgumentException("Invalid boost item costs.");
        if (user.Data.userColorfulPassV2?.colorfulPassId > 0) return 501;
        var boost = boosts.Preview();
        var autoMax = master.GetBoostConfig("boost_recovery_max_count");
        var costs = request.costs.GroupBy(c => c.resourceId)
            .Select(g => (Id: g.Key, Quantity: g.Sum(c => c.quantity))).ToArray();
        var recovered = 0;
        foreach (var cost in costs)
        {
            var item = master.GetBoostItem(cost.Id);
            if (item.recoveryValue <= 0) throw new InvalidOperationException("Invalid boost item recovery value.");
            if ((user.Data.userBoostItems?.SingleOrDefault(i => i.boostItemId == cost.Id)?.quantity ?? 0) < cost.Quantity)
                throw new ArgumentException("Insufficient boost items.");
            recovered = checked(recovered + checked(item.recoveryValue * cost.Quantity));
        }
        var current = checked(boost.current + recovered);
        if (current > master.GetBoostConfig("boost_max_count")) return 501;
        foreach (var cost in costs) resources.Consume("boost_item", cost.Id, cost.Quantity);
        boost.current = current;
        if (current >= autoMax) boost.recoveryAt = (ulong)user.Now;
        user.Data.userBoost = boost;
        user.MarkChanged(nameof(SuiteUser.userBoost));
        return 200;
    }

    public UserLive StartUserLive(UserLiveRequest request)
    {
        if (request.isAuto && request.boostCount <= 0)
            throw new ArgumentException("Auto Live requires boost consumption.");
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

    public LiveClearResponse ClearUserLive(string userLiveId, UserLiveClearRequest request)
    {
        user.MarkChanged(nameof(SuiteUser.userEventBreakTime));
        user.Private.UserLiveSessions.Remove(userLiveId, out var session);

        var fullCombo = session?.IsAuto != true && request.goodCount == 0 && request.badCount == 0 && request.missCount == 0;
        var fullPerfect = fullCombo && request.greatCount == 0;

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
        var userExpResult = BuildNoopExpResult();
        UserResource[] playerRankRewards = [];
        if (session != null)
        {
            boosts.Normalize();
            boosts.Consume(session.BoostCount);
            if (!session.IsAuto)
            {
                if (request.life > 0)
                    missions.RecordLiveRecords(request.maxCombo, master.ResolveMusicPlayLevel(session.MusicDifficultyId));
                if (request.life > 0 && fullCombo && master.ResolveMusicDifficultyType(session.MusicDifficultyId) == "easy" &&
                    !(user.Data.userMusicResults ?? []).Any(r => r.musicId == session.MusicId &&
                        r.musicDifficultyType == "easy" && r.fullComboFlg))
                    missions.RecordEasyFullCombo();
                highScoreFlg = UpdateUserMusicResult(session, request, fullCombo, fullPerfect);
            }
            deckCardExpResults = BuildDeckCardExpResults(session.DeckId);
            // 非 Auto 的 D 档及普通/Auto 的 C 档已有官方经验样本。
            var baseExp = scoreRank switch
            {
                "rank_d" when !session.IsAuto => 20,
                "rank_c" => 200,
                _ => 0
            };
            if (baseExp > 0 && boost.expRate > 0)
            {
                var addedExp = checked(baseExp * boost.expRate);
                (userExpResult, playerRankRewards) = GainPlayerExperience(addedExp);
                var deck = GetUserDeck(session.DeckId) ?? throw new ArgumentException("Live deck is missing.");
                var members = new[] { deck.member1, deck.member2, deck.member3, deck.member4, deck.member5 };
                deckCardExpResults = members.Select((id, index) => (id, index)).Where(slot => slot.id > 0)
                    .Select(slot => new DeckCardUpdateExpResult
                    {
                        index = slot.index + 1, expResult = cards.GainExperience(slot.id, checked(addedExp * 3))
                    }).ToArray();
            }
            grantedMusicAchievements = GrantUserMusicAchievements(session, request, scoreRank);
            scoreRankRewards = BuildScoreRankRewards(session.MusicDifficultyId, scoreRank, boost);
            musicAchievementRewards = BuildMusicAchievementRewards(grantedMusicAchievements);
            ApplyLiveRewards(scoreRankRewards);
            ApplyLiveRewards(musicAchievementRewards);
            missions.UpdateLiveMissionProgress(userLivePoint);
            missions.RecordLiveFinish(request.life > 0);
            if (!session.IsAuto)
                missions.RecordManualLiveClear();
            if (request.life > 0)
                RecordDeckLiveClear(session.DeckId);
            if (session.IsAuto)
            {
                user.Data.userAutoLive ??= new UserAutoLive();
                user.Data.userAutoLive.count = checked(user.Data.userAutoLive.count + 1);
                user.MarkChanged(nameof(SuiteUser.userAutoLive));
            }
            if (user.Data.userHonorMissions != null)
                user.MarkChanged(nameof(SuiteUser.userHonorMissions));
        }

        MergeLiveCharacterArchiveVoiceGroups(request.ingameCutinCharacterArchiveVoiceGroupIds);

        return new LiveClearResponse
        {
            ScoreRank = scoreRank,
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
            userExpResult = userExpResult,
            deckCardExpResults = deckCardExpResults,
            unitExpResults = [],
            userDeck = GetUserDeck(session?.DeckId),
            scoreRankRewards = scoreRankRewards,
            playerRankRewards = playerRankRewards,
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

    private void RecordDeckLiveClear(int deckId)
    {
        var deck = GetUserDeck(deckId) ?? throw new ArgumentException("Live deck is missing.");
        var characterId = master.GetCardCharacter(deck.leader);
        var memberCharacters = new[] { deck.member1, deck.member2, deck.member3, deck.member4, deck.member5 }
            .Where(id => id > 0).Select(master.GetCardCharacter).ToArray();
        if (memberCharacters.Distinct().Count() != memberCharacters.Length)
            throw new NotSupportedException("Live usage counts for repeated characters are not verified.");
        var counts = (user.Data.userCharacterLiveUsageCounts ?? []).ToList();
        void Increment(int id, string usageType)
        {
            var count = counts.SingleOrDefault(c => c.characterId == id && c.characterLiveUsageType == usageType);
            if (count == null)
            {
                count = new UserCharacterLiveUsageCount { characterId = id, characterLiveUsageType = usageType };
                counts.Add(count);
            }
            count.usageCount = checked(count.usageCount + 1);
        }
        Increment(characterId, "leader");
        // 官方五人编队换位样本仅累计前四个成员位。
        foreach (var cardId in new[] { deck.member1, deck.member2, deck.member3, deck.member4 }.Where(id => id > 0))
            Increment(master.GetCardCharacter(cardId), "member");
        user.Data.userCharacterLiveUsageCounts = counts.OrderBy(c => c.characterId)
            .ThenBy(c => c.characterLiveUsageType, StringComparer.Ordinal).ToArray();
        user.MarkChanged(nameof(SuiteUser.userCharacterLiveUsageCounts));
        missions.RecordCharacterLiveClear(characterId);
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
        var highScoreFlg = result == null || request.score > previousHighScore;
        var isNew = result == null;

        if (result == null)
        {
            result = new UserMusicResult
            {
                musicId = session.MusicId,
                musicDifficultyType = difficultyType,
                playType = "solo",
                playResult = "not_clear"
            };
            results.Add(result);
        }

        var before = (result.playType, result.playResult, result.highScore, result.fullComboFlg, result.fullPerfectFlg);
        result.playType = "solo";
        var playResult = BuildPlayResult(fullCombo, fullPerfect, request.life);
        if (PlayResultRank(playResult) > PlayResultRank(result.playResult))
            result.playResult = playResult;
        result.highScore = Math.Max(result.highScore, request.score);
        result.fullComboFlg = result.fullComboFlg || fullCombo;
        result.fullPerfectFlg = result.fullPerfectFlg || fullPerfect;

        user.Data.userMusicResults = results.ToArray();
        if (isNew || before != (result.playType, result.playResult, result.highScore, result.fullComboFlg, result.fullPerfectFlg))
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

    public (UpdateExpResult Result, UserResource[] Rewards) GainPlayerExperience(int addedExp)
    {
        if (addedExp < 0) throw new ArgumentOutOfRangeException(nameof(addedExp));
        var result = BuildNoopExpResult();
        var data = user.Data.userGamedata ?? throw new InvalidOperationException("Player data is missing.");
        var levels = master.GetUserLevels();
        if (levels.Length == 0) throw new InvalidOperationException("Player levels are missing.");
        var total = checked(data.totalExp + addedExp);
        if (total >= levels[^1].totalExp)
            throw new NotSupportedException("Maximum player rank experience is not verified.");
        var level = levels.Last(l => l.totalExp <= total);
        data.totalExp = total;
        data.rank = level.level;
        data.exp = total - level.totalExp;
        result.afterTotalExp = total;
        result.afterLevel = data.rank;
        result.afterExp = data.exp;
        user.MarkChanged(nameof(SuiteUser.userGamedata));
        var rewards = new List<UserResource>();
        if (data.rank > result.beforeLevel)
        {
            var conditions = (user.Data.userReleaseConditions ?? []).ToList();
            var released = conditions.Select(c => c.releaseConditionId).ToHashSet();
            foreach (var condition in master.GetPlayerRankReleaseConditions(result.beforeLevel, data.rank))
                if (released.Add(condition.id))
                    conditions.Add(new UserReleaseCondition
                    {
                        userId = user.UserId, releaseConditionId = condition.id, createdAt = user.Now
                    });
            if (conditions.Count != (user.Data.userReleaseConditions?.Length ?? 0))
            {
                user.Data.userReleaseConditions = conditions.ToArray();
                user.MarkChanged(nameof(SuiteUser.userReleaseConditions));
            }
            foreach (var reward in master.GetPlayerRankRewards(result.beforeLevel, data.rank))
            {
                var granted = resourceMaster.BuildResourcesFromBox("player_rank_reward", reward.resourceBoxId);
                resources.Grant(granted);
                rewards.AddRange(granted);
            }
            if (user.Data.userBoost is { } boost)
            {
                boost.current = checked(boost.current + (data.rank - result.beforeLevel) * master.GetRankUpBoostCount());
                if (boost.current >= master.GetBoostConfig("boost_recovery_max_count"))
                    boost.recoveryAt = (ulong)user.Now;
                user.MarkChanged(nameof(SuiteUser.userBoost));
            }
        }
        return (result, rewards.ToArray());
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
            liveMissionPeriodId = missionMaster.GetLiveMissionPeriodId(user.Now)
        };

    private static int PlayResultRank(string result) => result switch
    {
        "full_perfect" => 3,
        "full_combo" => 2,
        "clear" => 1,
        _ => 0
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
