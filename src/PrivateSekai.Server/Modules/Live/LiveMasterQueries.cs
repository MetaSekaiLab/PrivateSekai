extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Live;

public sealed class LiveMasterQueries(MasterData master)
{
    public bool IsLimitedMusicOutOfTerm(int musicId, long now)
    {
        var term = master.GetTable<MasterLimitedTimeMusic>("limitedTimeMusics").Rows.SingleOrDefault(m => m.musicId == musicId);
        return term != null && !IsWithinPeriod(now, term.startAt, term.endAt);
    }

    public bool IsAprilFoolVocal(int vocalId) =>
        master.GetTable<MasterMusicVocal>("musicVocals", v => v.id).FindById(vocalId)?.musicVocalType == "april_fool_2022";

    public bool IsAprilFoolSeason(long now) =>
        master.GetTable<MasterSpecialSeason>("specialSeasons").Rows.OrderByDescending(s => s.priority)
            .FirstOrDefault(s => IsWithinPeriod(now, s.startAt, s.endAt))?.specialSeasonType == "april_fool_2022";

    private static bool IsWithinPeriod(long now, long start, long end) =>
        start <= now && (end == 0 ? start != 0 : now < end);

    public bool HasMusicCategory(int musicId, string category) =>
        master.GetTable<MasterMusicCategory>("musicCategories").Rows
            .Any(c => c.musicId == musicId && c.musicCategoryName == category);

    public bool IsMusicVideoSelection(int musicId, int vocalId, string category) =>
        master.GetTable<MasterMusicVocal>("musicVocals", v => v.id).FindById(vocalId)?.musicId == musicId &&
        master.GetTable<MasterMusicCategory>("musicCategories").Rows
            .Any(c => c.musicId == musicId && c.musicCategoryName == category);

    public MasterBoostItem GetBoostItem(int id) => master.GetTable<MasterBoostItem>("boostItems", item => item.id)
        .FindById(id) ?? throw new ArgumentException("Unknown boost item.");

    public int GetBoostConfig(string key) => int.Parse(master.GetTable<MasterConfig>("configs").Rows
        .Single(c => c.configKey == key).value, System.Globalization.CultureInfo.InvariantCulture);

    public int GetChallengePlayableCount() => master.GetTable<MasterChallengeLive>("challengeLives").Rows.First().playableCount;

    public MasterEvent? GetPlayableEvent(long timestamp) => master.GetTable<MasterEvent>("events").Rows
        .SingleOrDefault(e => e.startAt <= timestamp && timestamp < e.aggregateAt);

    public int GetEventItemId(int eventId) => master.GetTable<MasterEventItem>("eventItems").Rows
        .Single(e => e.eventId == eventId).id;

    public int GetEventBreakMaxPoint(int id) => master.GetTable<MasterEventBreakTime>("eventBreakTimes").Rows
        .Single(e => e.id == id).maxPoint;

    public MasterReleaseCondition[] GetEventPointConditions(int eventId, int before, int after) =>
        master.GetTable<MasterReleaseCondition>("releaseConditions").Rows.Where(c =>
            c.releaseConditionType == "event_point" && c.releaseConditionTypeId == eventId &&
            c.releaseConditionTypeQuantity > before && c.releaseConditionTypeQuantity <= after).OrderBy(c => c.id).ToArray();

    public (int Point, int Items) CalculateChallengeEventPoint(int score)
    {
        if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));
        var configs = master.GetTable<MasterConfig>("configs").Rows;
        int Config(string key) => int.Parse(configs.Single(c => c.configKey == key).value,
            System.Globalization.CultureInfo.InvariantCulture);
        if (score > Config("event_score_ceiling"))
            throw new NotSupportedException("Challenge score above the event ceiling is not verified.");
        var rate = Config("challenge_live_event_point_rate");
        var reduction = Config("event_item_reduction");
        if (rate <= 0 || reduction <= 0) throw new InvalidOperationException("Invalid challenge event configuration.");
        var point = checked((100 + score / 20000) * rate);
        return (point, point / reduction);
    }

    public int GetChallengeLivePoint() => int.Parse(master.GetTable<MasterConfig>("configs").Rows
        .Single(c => c.configKey == "obtain_live_point_for_challenge_live").value,
        System.Globalization.CultureInfo.InvariantCulture);

    public MasterBirthdayParty? GetBirthdayParty(long timestamp)
    {
        var active = master.GetTable<MasterBirthdayParty>("birthdayParties").Rows
            .Where(p => p.startAt <= timestamp && (p.closedAt > timestamp || p.closedAt == 0 && p.startAt != 0)).ToArray();
        if (active.Length > 1) throw new NotSupportedException("Overlapping birthday rewards are not verified.");
        return active.SingleOrDefault();
    }

    public int GetChallengeLimitedRewardRate() => int.Parse(master.GetTable<MasterConfig>("configs").Rows
        .Single(c => c.configKey == "challenge_live_limited_term_score_rank_reward_rate").value,
        System.Globalization.CultureInfo.InvariantCulture);

    public int CalculateChallengeBasePoint(int score)
    {
        if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));
        var configs = master.GetTable<MasterConfig>("configs").Rows;
        var basePoint = int.Parse(configs.Single(c => c.configKey == "challenge_base_point").value,
            System.Globalization.CultureInfo.InvariantCulture);
        var divisor = int.Parse(configs.Single(c => c.configKey == "challenge_point_calc_value").value,
            System.Globalization.CultureInfo.InvariantCulture);
        if (basePoint <= 0 || divisor <= 0)
            throw new InvalidOperationException("Invalid challenge point configuration.");
        return checked(basePoint + score / divisor);
    }

    public string GetChallengeScoreRank(int musicDifficultyId, int score)
    {
        if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));
        return GetScoreRank(musicDifficultyId, score, "challenge_live");
    }

    public MasterChallengeLiveStage GetChallengeStage(int characterId, int rank) =>
        master.GetTable<MasterChallengeLiveStage>("challengeLiveStages").Rows
            .SingleOrDefault(s => s.characterId == characterId && s.rank == rank)
        ?? throw new NotSupportedException("Challenge stage is missing or enters unverified EX stages.");

    public MasterChallengeLiveHighScoreReward[] GetChallengeHighScoreRewards(int characterId, int score) =>
        master.GetTable<MasterChallengeLiveHighScoreReward>("challengeLiveHighScoreRewards").Rows
            .Where(r => r.characterId == characterId && r.highScore <= score)
            .OrderBy(r => r.highScore).ToArray();

    public MasterChallengeLivePlayDayRewardPeriod GetChallengePlayDayPeriod(long timestamp) =>
        master.GetTable<MasterChallengeLivePlayDayRewardPeriod>("challengeLivePlayDayRewardPeriods").Rows
            .Where(p => p.startAt < timestamp && timestamp < p.endAt)
            .OrderBy(p => p.priority).FirstOrDefault()
        ?? throw new InvalidOperationException("No active challenge play-day reward period.");

    public int GetDayChangeHour() => int.Parse(master.GetTable<MasterConfig>("configs").Rows
        .Single(c => c.configKey == "date_change_hour").value, System.Globalization.CultureInfo.InvariantCulture);

    public string GetChallengeResetDay() => master.GetTable<MasterConfig>("configs").Rows
        .Single(c => c.configKey == "challenge_live_reset_play_days_day_of_week").value;

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
                eventPointRate = boost.eventPointRate,
                bondsExpRate = boost.bondsExpRate
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

    public string BuildScoreRank(int musicDifficultyId, int score) =>
        GetScoreRank(musicDifficultyId, score, "solo");

    private string GetScoreRank(int musicDifficultyId, int score, string liveType)
    {
        var selected = GetMasterMusicDifficulty(musicDifficultyId)
            ?? throw new ArgumentException("Unknown music difficulty.");
        var reference = master.GetTable<MasterMusicDifficulty>("musicDifficulties", d => d.id).Rows
            .Where(d => d.musicId == selected.musicId)
            .Select(d => (Row: d, Kind: Enum.TryParse<MusicDifficulty>(d.musicDifficulty, out var kind) ? kind : MusicDifficulty.none))
            .Where(d => d.Kind > MusicDifficulty.hard)
            .OrderBy(d => d.Kind)
            .FirstOrDefault().Row
            ?? throw new InvalidOperationException("Missing score reference difficulty.");
        var thresholds = master.GetTable<MasterPlayLevelScore>("playLevelScores").Rows
            .SingleOrDefault(s => s.liveType == liveType && s.playLevel == reference.playLevel)
            ?? throw new InvalidOperationException("Missing score thresholds.");
        if (score >= thresholds.s) return "rank_s";
        if (score >= thresholds.a) return "rank_a";
        if (score >= thresholds.b) return "rank_b";
        return score >= thresholds.c ? "rank_c" : "rank_d";
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
