extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Characters;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Live;

public sealed class ChallengeLiveService(
    UserSession user,
    LiveMasterQueries master,
    ResourceMasterQueries resourceMaster,
    ResourceService resources,
    CharacterService characters,
    LiveService live,
    CardService cards)
{
    public (UpdateExpResult Player, DeckCardUpdateExpResult[] Cards, UserResource[] Rewards) GainExperience(
        UserChallengeLiveStartRequest start, UserChallengeLiveClearRequest clear)
    {
        if (start.isAuto || clear.life <= 0 || user.Data.userColorfulPassV2?.colorfulPassId > 0)
            throw new NotSupportedException("Challenge experience mode is not verified.");
        var addedExp = master.GetChallengeScoreRank(start.musicDifficultyId, clear.score) switch
        {
            "rank_d" => 400,
            "rank_c" => 4000,
            _ => throw new NotSupportedException("Challenge experience rank is not verified.")
        };
        int?[] slots = [start.leader, start.support1, start.support2, start.support3, start.support4];
        if (!start.leader.HasValue || slots.Where(c => c.HasValue).Distinct().Count() != slots.Count(c => c.HasValue))
            throw new ArgumentException("Invalid challenge experience recipients.");
        var (player, rewards) = live.GainPlayerExperience(addedExp);
        var results = slots.Select((id, index) => (id, index)).Where(s => s.id.HasValue)
            .Select(s => new DeckCardUpdateExpResult
            {
                index = s.index + 1, expResult = cards.GainExperience(s.id!.Value, checked(addedExp * 3))
            }).ToArray();
        return (player, results, rewards);
    }

    public UserChallengeLiveHighScoreResult UpdateHighScore(int characterId, int score)
    {
        if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));
        var results = (user.Data.userChallengeLiveSoloResults ?? []).ToList();
        var previous = results.SingleOrDefault(r => r.characterId == characterId);
        var before = previous?.highScore ?? 0;
        var after = Math.Max(before, score);
        var history = (user.Data.userChallengeLiveSoloHighScoreRewards ?? []).ToList();
        var owned = history.Where(r => r.characterId == characterId).ToArray();
        if (owned.Any(r => r.challengeLiveHighScoreStatus != "complete"))
            throw new NotSupportedException("Unknown challenge high-score reward status.");
        var ids = owned.Select(r => r.challengeLiveHighScoreRewardId).ToHashSet();
        var granted = new List<UserGrantedChallengeLiveHighScoreReward>();
        foreach (var row in master.GetChallengeHighScoreRewards(characterId, after).Where(r => !ids.Contains(r.id)))
        {
            var items = resourceMaster.BuildResourcesFromBox("challenge_live_high_score", row.resourceBoxId);
            if (items.Length == 0) throw new InvalidOperationException("Missing challenge high-score reward.");
            resources.Grant(items);
            granted.Add(new UserGrantedChallengeLiveHighScoreReward { challengeLiveHighScoreId = row.id, userResources = items });
            history.Add(new UserChallengeLiveHighScoreReward
            {
                characterId = characterId, challengeLiveHighScoreRewardId = row.id, challengeLiveHighScoreStatus = "complete"
            });
        }
        if (previous == null)
            results.Add(new UserChallengeLiveSoloResult { characterId = characterId, highScore = after });
        else
            previous.highScore = after;
        user.Data.userChallengeLiveSoloResults = results.ToArray();
        user.Data.userChallengeLiveSoloHighScoreRewards = history.ToArray();
        user.MarkChanged([nameof(SuiteUser.userChallengeLiveSoloResults), nameof(SuiteUser.userChallengeLiveSoloHighScoreRewards)]);
        return new UserChallengeLiveHighScoreResult { beforeHighScore = before, afterHighScore = after, rewards = granted.ToArray() };
    }

    public UserResource[] RecordFirstPlayDay(long playStartAt)
    {
        if (user.Data.userChallengeLivePlayDay != null)
            throw new NotSupportedException("Subsequent challenge attendance is not verified.");
        if (playStartAt <= 0 || playStartAt > user.Now)
            throw new ArgumentOutOfRangeException(nameof(playStartAt));
        var hour = master.GetDayChangeHour();
        if (hour is < 0 or > 23 || master.GetChallengeResetDay() != "MONDAY")
            throw new NotSupportedException("Unsupported challenge reset schedule.");
        // 客户端 SaveSystemData 固定采用 UTC+9，不使用宿主时区。
        var offset = TimeSpan.FromHours(9);
        var startDay = DateTimeOffset.FromUnixTimeMilliseconds(playStartAt).ToOffset(offset).AddHours(-hour).Date;
        var clearDay = DateTimeOffset.FromUnixTimeMilliseconds(user.Now).ToOffset(offset).AddHours(-hour).Date;
        var period = master.GetChallengePlayDayPeriod(playStartAt);
        if (startDay != clearDay || master.GetChallengePlayDayPeriod(user.Now).id != period.id)
            throw new NotSupportedException("Challenge clear across a daily or reward-period boundary is not verified.");
        var choices = (period.challengeLivePlayDayRewards ?? []).Where(r => r.playDays == 1).ToArray();
        if (choices.Length != 1)
            throw new NotSupportedException("Challenge reward selection is not verified.");
        var rewards = resourceMaster.BuildResourcesFromBox("challenge_live_play_day_reward", choices[0].resourceBoxId);
        if (rewards.Length == 0) throw new InvalidOperationException("Missing challenge play-day reward.");
        var days = ((int)DayOfWeek.Monday - (int)startDay.DayOfWeek + 7) % 7;
        if (days == 0) days = 7;
        var reset = new DateTimeOffset(startDay.AddDays(days).AddHours(hour), offset).ToUnixTimeMilliseconds();
        resources.Grant(rewards);
        user.Data.userChallengeLivePlayDay = new UserChallengeLivePlayDay
        {
            playDays = 1, challengeLivePlayDayRewardStatus = "received",
            playDaysResetAt = reset, lastPlayStartAt = playStartAt
        };
        user.MarkChanged(nameof(SuiteUser.userChallengeLivePlayDay));
        return rewards;
    }

    public UserChallengeLiveStageResult AdvanceStage(UserChallengeLiveStartRequest start, UserChallengeLiveClearRequest clear)
    {
        if (start.isAuto || clear.life <= 0 || user.Data.userColorfulPassV2?.colorfulPassId > 0)
            throw new NotSupportedException("Challenge point mode is not verified.");
        return AdvanceStage(start.characterId, master.CalculateChallengeBasePoint(clear.score));
    }

    public UserChallengeLiveStageResult AdvanceStage(int characterId, int addPoint)
    {
        if (addPoint <= 0) throw new ArgumentOutOfRangeException(nameof(addPoint));
        var stages = (user.Data.userChallengeLiveSoloStages ?? []).ToList();
        var owned = stages.Where(s => s.characterId == characterId).ToArray();
        if (owned.Any(s => s.challengeLiveStageType != "normal"))
            throw new NotSupportedException("Challenge EX stages are not verified.");
        var current = owned.SingleOrDefault(s => s.challengeLiveStageStatus == "in_progress");
        if (owned.Length > 0 && current == null)
            throw new InvalidOperationException("Missing current challenge stage.");
        var beforeRank = current?.rank ?? 1;
        var beforePoint = current?.point ?? 0;
        var rank = beforeRank;
        var point = checked(beforePoint + addPoint);
        var rewards = new List<UserResource>();
        var characterExp = 0;
        if (current != null) stages.Remove(current);
        while (true)
        {
            var row = master.GetChallengeStage(characterId, rank);
            if (row.nextStageChallengePoint <= 0 || row.completeStageCharacterExp < 0)
                throw new InvalidOperationException("Invalid challenge stage thresholds.");
            var complete = point >= row.nextStageChallengePoint;
            stages.Add(new UserChallengeLiveSoloStage
            {
                characterId = characterId, challengeLiveStageType = "normal", rank = rank,
                challengeLiveStageId = row.id, challengeLiveStageStatus = complete ? "complete" : "in_progress",
                point = complete ? row.nextStageChallengePoint : point
            });
            if (!complete) break;
            var stageRewards = resourceMaster.BuildResourcesFromBox("challenge_live_stage", row.completeStageResourceBoxId);
            if (row.completeStageResourceBoxId > 0 && stageRewards.Length == 0)
                throw new InvalidOperationException("Missing challenge stage rewards.");
            rewards.AddRange(stageRewards);
            characterExp = checked(characterExp + row.completeStageCharacterExp);
            point -= row.nextStageChallengePoint;
            rank++;
        }
        resources.Grant(rewards);
        user.Data.userChallengeLiveSoloStages = stages.ToArray();
        user.MarkChanged(nameof(SuiteUser.userChallengeLiveSoloStages));
        var (experience, rankRewards) = characters.Gain(characterId, characterExp);
        return new UserChallengeLiveStageResult
        {
            addPoint = addPoint, beforeRank = beforeRank, beforePoint = beforePoint,
            afterRank = rank, afterPoint = point, challengeStageRewards = rewards.ToArray(),
            characterExpResult = experience, characterRankUpRewards = rankRewards
        };
    }

    public (int Status, UserChallengeLiveStartResponse? Response) Start(UserChallengeLiveStartRequest request)
    {
        var deck = (user.Data.userChallengeLiveSoloDecks ?? []).SingleOrDefault(d => d.characterId == request.characterId);
        if (deck == null) return (404, null);
        if ((user.Data.userChallengeLivePlayStatuses ?? []).Sum(s => s.playCount) >= master.GetChallengePlayableCount())
            return (409, null);
        int?[] slots = [request.leader, request.support1, request.support2, request.support3, request.support4];
        int?[] savedSlots = [deck.leader, deck.support1, deck.support2, deck.support3, deck.support4];
        // 只接入已有官方开局样本的编队和模式。
        if (request.isAuto || !slots.SequenceEqual(savedSlots) || !request.leader.HasValue ||
            slots.Skip(2).Any(c => c.HasValue))
            return (501, null);
        var difficulty = master.GetMasterMusicDifficulty(request.musicDifficultyId);
        if (difficulty == null || difficulty.musicId != request.musicId)
            throw new ArgumentException("Challenge music difficulty does not match the song.");
        SaveDeck(request.characterId, deck);
        foreach (var previous in user.Private.ChallengeLiveSessions.Where(s => s.Value.characterId == request.characterId).Select(s => s.Key).ToArray())
            user.Private.ChallengeLiveSessions.Remove(previous);
        var id = Guid.NewGuid().ToString();
        user.Private.ChallengeLiveSessions[id] = request;
        var statuses = (user.Data.userChallengeLivePlayStatuses ?? []).ToList();
        statuses.RemoveAll(s => s.characterId == request.characterId);
        statuses.Add(new UserChallengeLivePlayStatus
        {
            userChallengeLiveId = id, characterId = request.characterId,
            musicId = request.musicId, musicDifficultyId = request.musicDifficultyId, musicVoiceId = request.musicVocalId,
            liveStatus = "start", playCount = 0, playStartAt = user.Now, isAuto = false
        });
        user.Data.userChallengeLivePlayStatuses = statuses.OrderBy(s => s.characterId).ToArray();
        var members = slots.Where(c => c.HasValue).Select(c => c!.Value).Append(request.leader.Value);
        return (200, new UserChallengeLiveStartResponse
        {
            userChallengeLiveId = id,
            skills = members.Select((cardId, index) => new IngameLotterySkill { seq = index + 1, cardId = cardId }).ToArray()
        });
    }

    public bool TryUnlockFirstCharacter(int characterId)
    {
        const string behaviorType = "challenge_live_character_force_release";
        var character = master.GetChallengeCharacter(characterId);
        var behaviors = (user.Data.userOneTimeBehaviors ?? []).ToList();
        if (behaviors.Any(b => b.oneTimeBehaviorType == behaviorType) ||
            (user.Data.userGamedata?.rank ?? 0) < master.GetFirstChallengeUnlockRank())
            return false;
        var conditions = (user.Data.userReleaseConditions ?? []).ToList();
        if (conditions.Any(c => c.releaseConditionId == character.orReleaseConditionId))
            return false;
        conditions.Add(new UserReleaseCondition { userId = user.UserId, releaseConditionId = character.orReleaseConditionId, createdAt = user.Now });
        behaviors.Add(new UserOneTimeBehavior { userId = user.UserId, oneTimeBehaviorType = behaviorType });
        user.Data.userReleaseConditions = conditions.OrderBy(c => c.releaseConditionId).ToArray();
        user.Data.userOneTimeBehaviors = behaviors.ToArray();
        user.MarkChanged([nameof(SuiteUser.userReleaseConditions), nameof(SuiteUser.userOneTimeBehaviors)]);
        return true;
    }

    public UserChallengeLiveSoloDeck SaveDeck(int characterId, UserChallengeLiveSoloDeck request)
    {
        if (request.characterId != characterId)
            throw new ArgumentException("Challenge deck character does not match the path.");
        var character = master.GetChallengeCharacter(characterId);
        var released = (user.Data.userReleaseConditions ?? []).Select(c => c.releaseConditionId).ToHashSet();
        if (!released.Contains(character.releaseConditionId) && !released.Contains(character.orReleaseConditionId))
            throw new InvalidOperationException("Challenge character is locked.");
        int?[] slots = [request.leader, request.support1, request.support2, request.support3, request.support4];
        var cards = slots.Where(c => c.HasValue).Select(c => c!.Value).ToArray();
        var owned = (user.Data.userCards ?? []).Select(c => c.cardId).ToHashSet();
        var rank = (user.Data.userCharacters ?? []).SingleOrDefault(c => c.characterId == characterId)?.characterRank ?? 0;
        var limit = master.GetChallengeCardLimit(characterId, rank);
        if (!request.leader.HasValue || cards.Distinct().Count() != cards.Length ||
            cards.Any(c => !owned.Contains(c) || master.GetCardCharacter(c) != characterId) ||
            slots.Skip(limit).Any(c => c.HasValue))
            throw new ArgumentException("Invalid challenge deck members or locked slots.");
        var deck = new UserChallengeLiveSoloDeck
        {
            characterId = characterId, leader = request.leader,
            support1 = request.support1, support2 = request.support2,
            support3 = request.support3, support4 = request.support4
        };
        var decks = (user.Data.userChallengeLiveSoloDecks ?? []).ToList();
        decks.RemoveAll(d => d.characterId == characterId);
        decks.Add(deck);
        user.Data.userChallengeLiveSoloDecks = decks.OrderBy(d => d.characterId).ToArray();
        user.Data.userChallengeLivePlayStatuses ??= [];
        user.Data.userChallengeLiveSoloResults ??= [];
        user.Data.userChallengeLiveSoloStages ??= [];
        user.Data.userChallengeLiveSoloHighScoreRewards ??= [];
        user.MarkChanged([
            nameof(SuiteUser.userChallengeLivePlayDay),
            nameof(SuiteUser.userChallengeLivePlayStatuses), nameof(SuiteUser.userChallengeLiveSoloDecks),
            nameof(SuiteUser.userChallengeLiveSoloResults), nameof(SuiteUser.userChallengeLiveSoloStages),
            nameof(SuiteUser.userChallengeLiveSoloHighScoreRewards)
        ]);
        return deck;
    }
}
