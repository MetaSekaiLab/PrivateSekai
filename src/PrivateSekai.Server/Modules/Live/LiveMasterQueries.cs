extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Live;

public sealed class LiveMasterQueries(MasterData master)
{
    public MasterBoostItem GetBoostItem(int id) => master.GetTable<MasterBoostItem>("boostItems", item => item.id)
        .FindById(id) ?? throw new ArgumentException("Unknown boost item.");

    public int GetBoostConfig(string key) => int.Parse(master.GetTable<MasterConfig>("configs").Rows
        .Single(c => c.configKey == key).value, System.Globalization.CultureInfo.InvariantCulture);

    public int GetChallengePlayableCount() => master.GetTable<MasterChallengeLive>("challengeLives").Rows.First().playableCount;

    public MasterChallengeLiveStage GetChallengeStage(int characterId, int rank) =>
        master.GetTable<MasterChallengeLiveStage>("challengeLiveStages").Rows
            .SingleOrDefault(s => s.characterId == characterId && s.rank == rank)
        ?? throw new NotSupportedException("Challenge stage is missing or enters unverified EX stages.");

    public int GetFirstChallengeUnlockRank()
    {
        var behavior = master.GetTable<MasterOneTimeBehavior>("oneTimeBehaviors").Rows
            .Single(b => b.oneTimeBehaviorType == "challenge_live_character_force_release");
        var condition = master.GetTable<MasterReleaseCondition>("releaseConditions", c => c.id)
            .FindById(behavior.releaseConditionId)
            ?? throw new InvalidOperationException("Missing first challenge unlock condition.");
        if (behavior.releaseConditionId2 != 0 || condition.releaseConditionType != "user_rank")
            throw new NotSupportedException("Unsupported first challenge unlock condition.");
        return condition.releaseConditionTypeLevel;
    }

    public MasterChallengeLiveCharacter GetChallengeCharacter(int characterId) =>
        master.GetTable<MasterChallengeLiveCharacter>("challengeLiveCharacters").Rows
            .SingleOrDefault(c => c.characterId == characterId)
        ?? throw new ArgumentException("Unknown challenge character.");

    public int GetCardCharacter(int cardId) => master.GetTable<MasterCard>("cards", c => c.id)
        .FindById(cardId)?.characterId ?? throw new ArgumentException("Unknown card.");

    public int GetChallengeCardLimit(int characterId, int characterRank)
    {
        var limit = int.Parse(master.GetTable<MasterConfig>("configs").Rows
            .Single(c => c.configKey == "default_challenge_live_deck_limit").value,
            System.Globalization.CultureInfo.InvariantCulture);
        foreach (var deck in master.GetTable<MasterChallengeLiveDeck>("challengeLiveDecks").Rows
                     .Where(d => d.characterId == characterId))
        {
            var condition = master.GetTable<MasterReleaseCondition>("releaseConditions", c => c.id)
                .FindById(deck.releaseConditionId)
                ?? throw new InvalidOperationException("Missing challenge deck condition.");
            if (condition.releaseConditionType != "character_rank" || condition.releaseConditionTypeId != characterId)
                throw new NotSupportedException("Unsupported challenge deck condition.");
            if (characterRank >= condition.releaseConditionTypeLevel)
                limit = Math.Max(limit, deck.cardLimit);
        }
        return limit;
    }

    public MasterLevel[] GetUserLevels() => master.GetTable<MasterLevel>("levels").Rows
        .Where(l => l.levelType == "user").OrderBy(l => l.level).ToArray();

    public MasterPlayerRankReward[] GetPlayerRankRewards(int before, int after) =>
        master.GetTable<MasterPlayerRankReward>("playerRankRewards").Rows
            .Where(r => r.playerRank > before && r.playerRank <= after)
            .OrderBy(r => r.playerRank).ThenBy(r => r.seq).ToArray();

    public MasterReleaseCondition[] GetPlayerRankReleaseConditions(int before, int after) =>
        master.GetTable<MasterReleaseCondition>("releaseConditions").Rows
            .Where(c => c.releaseConditionType == "user_rank" &&
                c.releaseConditionTypeLevel > before && c.releaseConditionTypeLevel <= after)
            .OrderBy(c => c.id).ToArray();

    public int GetRankUpBoostCount() => int.Parse(master.GetTable<MasterConfig>("configs").Rows
        .Single(c => c.configKey == "rank_up_recover_boost_count").value,
        System.Globalization.CultureInfo.InvariantCulture);

    public MasterBoost BuildMasterBoost(int boostCount)
    {
        foreach (var boost in master.GetTable<MasterBoost>("boosts", b => b.id).Rows)
        {
            if (boost.costBoost != boostCount)
                continue;

            return new MasterBoost
            {
                id = boost.id,
                costBoost = boostCount,
                isEventOnly = boost.isEventOnly,
                expRate = boost.expRate,
                rewardRate = boost.rewardRate,
                livePointRate = boost.livePointRate,
                eventPointRate = boost.eventPointRate
            };
        }

        var rate = boostCount <= 0 ? 1 : boostCount * 5;
        return new MasterBoost
        {
            id = boostCount + 1,
            costBoost = boostCount,
            isEventOnly = false,
            expRate = rate,
            rewardRate = rate,
            livePointRate = rate,
            eventPointRate = rate
        };
    }

    public MasterMusicDifficulty? GetMasterMusicDifficulty(int musicDifficultyId) =>
        master.GetTable<MasterMusicDifficulty>("musicDifficulties", d => d.id).FindById(musicDifficultyId);

    public string ResolveMusicDifficultyType(int musicDifficultyId) =>
        GetMasterMusicDifficulty(musicDifficultyId)?.musicDifficulty ?? musicDifficultyId.ToString();

    public int ResolveMusicTotalNoteCount(int musicDifficultyId) =>
        GetMasterMusicDifficulty(musicDifficultyId)?.totalNoteCount ?? 0;

    public int ResolveMusicPlayLevel(int musicDifficultyId) =>
        GetMasterMusicDifficulty(musicDifficultyId)?.playLevel ?? 0;

    public int[] ResolveMusicAchievementIds(int musicDifficultyId, int maxCombo, string scoreRank)
    {
        var difficultyType = ResolveMusicDifficultyType(musicDifficultyId);
        var totalNoteCount = ResolveMusicTotalNoteCount(musicDifficultyId);
        var achievementIds = new List<int>();

        foreach (var achievement in master.GetTable<MasterMusicAchievement>("musicAchievements", a => a.id).Rows)
        {
            var type = achievement.musicAchievementType;
            var value = achievement.musicAchievementTypeValue;
            if (type == "score_rank" && IsScoreRankReached(scoreRank, value))
            {
                achievementIds.Add(achievement.id);
                continue;
            }

            if (type != "combo" ||
                !string.Equals(achievement.musicDifficultyType, difficultyType, StringComparison.Ordinal) ||
                totalNoteCount <= 0 ||
                value == null ||
                !double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var ratio))
                continue;

            if (maxCombo >= (int)Math.Ceiling(totalNoteCount * ratio))
                achievementIds.Add(achievement.id);
        }

        return achievementIds.OrderBy(id => id).ToArray();
    }

    public int GetMusicAchievementResourceBoxId(int musicAchievementId) =>
        master.GetTable<MasterMusicAchievement>("musicAchievements", a => a.id)
            .FindById(musicAchievementId)?.resourceBoxId ?? 0;

    public string BuildScoreRank(int musicDifficultyId, int score)
    {
        var playLevel = ResolveMusicPlayLevel(musicDifficultyId);
        if (playLevel > 0)
        {
            foreach (var scoreThreshold in master.GetTable<MasterPlayLevelScore>("playLevelScores").Rows)
            {
                if (!string.Equals(scoreThreshold.liveType, "solo", StringComparison.Ordinal) ||
                    scoreThreshold.playLevel != playLevel)
                    continue;

                if (score >= scoreThreshold.s)
                    return "rank_s";
                if (score >= scoreThreshold.a)
                    return "rank_a";
                if (score >= scoreThreshold.b)
                    return "rank_b";
                if (score >= scoreThreshold.c)
                    return "rank_c";
                return "rank_d";
            }
        }

        return BuildScoreRank(score);
    }

    public static string BuildScoreRank(int score) =>
        score switch
        {
            >= 600_000 => "rank_s",
            >= 300_000 => "rank_a",
            >= 150_000 => "rank_b",
            >= 50_000 => "rank_c",
            _ => "rank_d"
        };

    private static bool IsScoreRankReached(string scoreRank, string? achievementRank)
    {
        var actual = ScoreRankOrder(scoreRank);
        var required = ScoreRankOrder(achievementRank);
        return required > 0 && actual >= required;
    }

    private static int ScoreRankOrder(string? rank)
    {
        var normalized = rank?.ToUpperInvariant();
        return normalized switch
        {
            "RANK_SS" or "RANK_S" or "RANK_S_PLUS" => 4,
            "RANK_A" => 3,
            "RANK_B" => 2,
            "RANK_C" => 1,
            _ => 0
        };
    }
}
