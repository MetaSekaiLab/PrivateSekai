using PrivateSekai.Config;
using PrivateSekai.Models;
using PrivateSekai.Models.Master;

namespace PrivateSekai.Services.Master;

public sealed class MasterDataManager
{
    public static MasterDataManager Instance { get; private set; } = null!;

    private readonly MasterTableCache _cache;

    public MasterDataManager(MasterCacheConfig config)
    {
        _cache = new MasterTableCache(config, ServerConfig.SekaiMasterDbDiffPath);
        WarmPinnedTables(config);
    }

    public static void Bind(MasterDataManager instance) => Instance = instance;

    public IDisposable BeginRequest() => _cache.BeginRequest();

    public void ClearCache() => _cache.Clear();

    public List<int> GetCardEpisodeIds(int cardId)
    {
        var episodes = _cache.GetTable<MasterCardEpisode>("cardEpisodes", e => e.id).Rows;
        var ids = episodes.Where(e => e.cardId == cardId).Select(e => e.id).ToList();
        while (ids.Count < 2)
            ids.Add(0);
        return ids;
    }

    public MasterCardEpisode? GetMasterCardEpisode(int cardEpisodeId) =>
        _cache.GetTable<MasterCardEpisode>("cardEpisodes", e => e.id).FindById(cardEpisodeId);

    public int GetConfigInt(string configKey, int fallback = 0)
    {
        var config = _cache.GetTable<MasterConfigRow>("configs").Rows
            .FirstOrDefault(c => string.Equals(c.configKey, configKey, StringComparison.Ordinal));

        return int.TryParse(config?.value, out var value) ? value : fallback;
    }

    public MasterGacha? GetMasterGacha(int gachaId) =>
        _cache.GetTable<MasterGacha>("gachas", g => g.id).FindById(gachaId);

    public MasterGachaBehavior? GetMasterGachaBehavior(MasterGacha? gacha, int gachaBehaviorId) =>
        gacha?.gachaBehaviors?.FirstOrDefault(b => b.id == gachaBehaviorId);

    public MasterCard? GetMasterCard(int cardId) =>
        _cache.GetTable<MasterCard>("cards", c => c.id).FindById(cardId);

    public IReadOnlyList<MasterCard> GetMasterCards() =>
        _cache.GetTable<MasterCard>("cards", c => c.id).Rows;

    public string? GetCardRarityType(int cardId) =>
        GetMasterCard(cardId)?.cardRarityType;

    public int GetCardMaxLevel(int cardId, bool specialTrained)
    {
        var rarityType = GetCardRarityType(cardId);
        if (rarityType == null)
            return 1;

        var rarity = _cache.GetTable<MasterCardRarity>("cardRarities")
            .Rows
            .FirstOrDefault(r => string.Equals(r.cardRarityType, rarityType, StringComparison.Ordinal));

        if (rarity == null)
            return 1;

        return specialTrained && rarity.trainingMaxLevel.HasValue
            ? rarity.trainingMaxLevel.Value
            : rarity.maxLevel;
    }

    public int GetPracticeTicketExp(int practiceTicketId) =>
        _cache.GetTable<MasterPracticeTicket>("practiceTickets", t => t.id)
            .FindById(practiceTicketId)?.exp ?? 0;

    public int GetCardLevelFromTotalExp(int totalExp, int maxLevel)
    {
        var levels = _cache.GetTable<MasterLevel>("levels")
            .Rows
            .Where(l => string.Equals(l.levelType, "card", StringComparison.Ordinal) && l.level <= maxLevel)
            .OrderBy(l => l.level);

        var level = 1;
        foreach (var row in levels)
        {
            if (row.totalExp > totalExp)
                break;

            level = row.level;
        }

        return level;
    }

    public int GetCardLevelTotalExp(int level)
    {
        if (level <= 1)
            return 0;

        return _cache.GetTable<MasterLevel>("levels")
            .Rows
            .FirstOrDefault(l => string.Equals(l.levelType, "card", StringComparison.Ordinal) && l.level == level)
            ?.totalExp ?? 0;
    }

    public int GetCardLevelMaxTotalExp(int maxLevel) =>
        GetCardLevelTotalExp(maxLevel);

    public MasterLessonCost[] GetMasterLessonCosts(IEnumerable<int>? costIds)
    {
        if (costIds == null)
            return [];

        var requested = costIds.Where(id => id > 0).ToHashSet();
        if (requested.Count == 0)
            return [];

        return _cache.GetTable<MasterLesson>("masterLessons")
            .Rows
            .SelectMany(row => row.costs ?? [])
            .Where(cost => requested.Contains(cost.id))
            .ToArray();
    }

    public MasterLessonReward[] GetMasterLessonRewards(int cardId, int beforeMasterRank, int afterMasterRank)
    {
        if (afterMasterRank <= beforeMasterRank)
            return [];

        return _cache.GetTable<MasterLessonReward>("masterLessonRewards", r => r.id)
            .Rows
            .Where(reward =>
                reward.cardId == cardId &&
                reward.masterRank > beforeMasterRank &&
                reward.masterRank <= afterMasterRank)
            .OrderBy(reward => reward.masterRank)
            .ToArray();
    }

    public int GetCardExchangeResourceBoxId(string? cardRarityType)
    {
        if (cardRarityType == null)
            return 0;

        return _cache.GetTable<MasterCardExchangeResource>("cardExchangeResources")
            .Rows
            .Where(row => string.Equals(row.cardRarityType, cardRarityType, StringComparison.Ordinal))
            .OrderBy(row => row.seq)
            .FirstOrDefault()
            ?.resourceBoxId ?? 0;
    }

    public UserResource[] GetCardExchangeResources(string? cardRarityType)
    {
        var resourceBoxId = GetCardExchangeResourceBoxId(cardRarityType);
        return BuildResourcesFromBox("card_exchange_resource", resourceBoxId);
    }

    public MasterBeginnerMissionV2? GetBeginnerMissionV2(int missionId) =>
        _cache.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s", m => m.id)
            .FindById(missionId);

    public int[] GetCardCostume3dIds(int cardId) =>
        _cache.GetTable<MasterCardCostume3D>("cardCostume3ds")
            .Rows
            .Where(c => c.cardId == cardId && c.costume3dId > 0)
            .Select(c => c.costume3dId)
            .ToArray();

    public int ResolveGachaCeilItemId(MasterGacha? gacha, int gachaId)
    {
        if (gacha?.gachaCeilItemId > 0)
            return gacha.gachaCeilItemId;

        return _cache.GetTable<MasterGachaCeilItem>("gachaCeilItems", i => i.id).Rows
            .FirstOrDefault(i => i.gachaId == gachaId)?.id ?? 0;
    }

    public MasterGachaCeilExchange? GetMasterGachaCeilExchange(int gachaCeilExchangeId)
    {
        foreach (var summary in _cache.GetTable<MasterGachaCeilExchangeSummary>("gachaCeilExchangeSummaries", s => s.id).Rows)
        {
            var exchange = summary.gachaCeilExchanges?.FirstOrDefault(e => e.id == gachaCeilExchangeId);
            if (exchange != null)
                return exchange;
        }

        return null;
    }

    public MasterShopItem? GetMasterShopItem(int shopId, int shopItemId) =>
        _cache.GetTable<MasterShopItem>("shopItems", i => i.id).Rows
            .FirstOrDefault(i => i.id == shopItemId && i.shopId == shopId);

    public IReadOnlyList<MasterMusicVocal> GetMasterMusicVocals() =>
        _cache.GetTable<MasterMusicVocal>("musicVocals", v => v.id).Rows;

    public MasterMusicVocal? GetMasterMusicVocal(int musicVocalId) =>
        _cache.GetTable<MasterMusicVocal>("musicVocals", v => v.id).FindById(musicVocalId);

    public int GetMysekaiToolMaxDurability(int mysekaiToolId) =>
        _cache.GetTable<MasterMysekaiTool>("mysekaiTools", t => t.id)
            .FindById(mysekaiToolId)?.maxDurability ?? 0;

    public MasterBoost BuildMasterBoost(int boostCount)
    {
        foreach (var boost in _cache.GetTable<MasterBoostRow>("boosts", b => b.id).Rows)
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
            eventPointRate = rate,
            bondsExpRate = rate
        };
    }

    public MasterMusicDifficulty? GetMasterMusicDifficulty(int musicDifficultyId) =>
        _cache.GetTable<MasterMusicDifficulty>("musicDifficulties", d => d.id).FindById(musicDifficultyId);

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

        foreach (var achievement in _cache.GetTable<MasterMusicAchievement>("musicAchievements", a => a.id).Rows)
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
        _cache.GetTable<MasterMusicAchievement>("musicAchievements", a => a.id)
            .FindById(musicAchievementId)?.resourceBoxId ?? 0;

    public UserResource[] BuildResourcesFromBox(string purpose, int resourceBoxId)
    {
        if (resourceBoxId <= 0)
            return [];

        foreach (var box in _cache.GetTable<MasterResourceBox>("resourceBoxes", b => b.id).Rows)
        {
            if (!string.Equals(box.resourceBoxPurpose, purpose, StringComparison.Ordinal) ||
                box.id != resourceBoxId ||
                box.details == null)
                continue;

            return box.details
                .Select(detail => new UserResource
                {
                    resourceType = detail.resourceType,
                    resourceId = detail.resourceId,
                    resourceLevel = detail.resourceLevel,
                    quantity = detail.resourceQuantity
                })
                .ToArray();
        }

        return [];
    }

    public UserResource[] BuildResourcesFromBoxes(string purpose, IEnumerable<int>? resourceBoxIds)
    {
        if (resourceBoxIds == null)
            return [];

        return resourceBoxIds
            .SelectMany(resourceBoxId => BuildResourcesFromBox(purpose, resourceBoxId))
            .ToArray();
    }

    public int GetCurrentLiveMissionPeriodId()
    {
        var current = 0;
        foreach (var pass in _cache.GetTable<MasterLiveMissionPass>("liveMissionPasses", p => p.id).Rows)
            current = Math.Max(current, pass.liveMissionPeriodId);
        return current;
    }

    public string BuildScoreRank(int musicDifficultyId, int score)
    {
        var playLevel = ResolveMusicPlayLevel(musicDifficultyId);
        if (playLevel > 0)
        {
            foreach (var scoreThreshold in _cache.GetTable<MasterPlayLevelScore>("playLevelScores").Rows)
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

    private void WarmPinnedTables(MasterCacheConfig config)
    {
        foreach (var table in config.PinTables)
        {
            switch (table)
            {
                case "cards":
                    _ = GetMasterCards();
                    break;
                case "gachas":
                    _ = _cache.GetTable<MasterGacha>("gachas", g => g.id).Rows;
                    break;
                case "resourceBoxes":
                    _ = _cache.GetTable<MasterResourceBox>("resourceBoxes", b => b.id).Rows;
                    break;
                case "shopItems":
                    _ = _cache.GetTable<MasterShopItem>("shopItems", i => i.id).Rows;
                    break;
            }
        }
    }

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
