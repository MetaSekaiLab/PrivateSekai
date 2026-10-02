extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Live;

public sealed class LiveMasterQueries(MasterData master)
{
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
