extern alias game;

using System.Globalization;
using System.Text.Json;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Client.Realtime;

public sealed class LiveDeckPower(string directory)
{
    private readonly MasterData master = new(new MasterCacheConfig(), directory);
    private readonly MasterMysekaiFixtureGameCharacterPerformanceBonusLimit? fixtureLimit =
        JsonSerializer.Deserialize<MasterMysekaiFixtureGameCharacterPerformanceBonusLimit>(
            File.ReadAllText(Path.Combine(directory, "mysekaiFixtureGameCharacterPerformanceBonusLimit.json")), DumpJson.Options);

    public int Calculate(SuiteUser user, long now)
    {
        using var scope = master.BeginRequest();
        var deck = user.userDecks.Single(d => d.deckId == user.userGamedata.deck);
        var members = new[] { deck.member1, deck.member2, deck.member3, deck.member4, deck.member5 };
        var cards = master.GetTable<MasterCard>("cards", c => c.id);
        var characters = master.GetTable<MasterGameCharacter>("gameCharacters", c => c.id).ById!;
        var definitions = members.Select(id => cards.FindById(id) ?? throw new InvalidDataException("队伍卡牌缺少 master。")).ToArray();
        var effects = (user.userAreas ?? []).SelectMany(a => a.areaItems ?? [])
            .SelectMany(item => master.GetTable<MasterAreaItemLevel>("areaItemLevels").Rows
                .Where(e => e.areaItemId == item.areaItemId && e.level == item.level)).ToArray();
        var result = 0;
        foreach (var card in definitions)
        {
            var owned = user.userCards.Single(c => c.cardId == card.id);
            var values = BaseParameters(user, card, owned);
            var basic = values.Sum();
            var rank = user.userCharacters?.SingleOrDefault(c => c.characterId == card.characterId)?.characterRank ?? 1;
            var bonus = master.GetTable<MasterCharacterRank>("characterRanks").Rows
                .Single(c => c.characterId == card.characterId && c.characterRank == rank);
            var rates = new[] { bonus.power1BonusRate, bonus.power2BonusRate, bonus.power3BonusRate };
            result += basic + values.Select((value, index) => (int)MathF.Floor((float)(rates[index] * 0.01) * value)).Sum()
                + LiveAreaPower.Calculate(card, characters, definitions, effects, values[0], values[1], values[2])
                + GateBonus(user, card, characters[card.characterId].unit, basic)
                + FixtureBonus(user, card.characterId, basic, now);
        }
        foreach (var owned in user.userHonors ?? [])
        {
            var honor = master.GetTable<MasterHonor>("honors", h => h.id).FindById(owned.honorId);
            if (honor?.levels?.Any(l => l.bonus > 0) == true)
                result += honor.levels.FirstOrDefault(l => l.level == owned.level)?.bonus ?? 0;
        }
        result = Math.Max(0, result);
        var active = master.GetTable<MasterEvent>("events").Rows.FirstOrDefault(e => Within(now, e.startAt, e.aggregateAt));
        var limit = active == null ? null : master.GetTable<MasterEventTotalPowerLimit>("eventTotalPowerLimits").Rows
            .FirstOrDefault(l => l.eventId == active.id);
        return limit == null ? result : Math.Min(result, limit.upperTotalPower);
    }

    private int[] BaseParameters(SuiteUser user, MasterCard card, UserCard owned)
    {
        var values = new[] { "param1", "param2", "param3" }.Select(type => card.cardParameters
            .Where(p => p.cardParameterType == type).OrderBy(p => p.cardLevel).ElementAt(owned.level - 1).power).ToArray();
        if (owned.specialTrainingStatus == "done")
        {
            values[0] += card.specialTrainingPower1BonusFixed;
            values[1] += card.specialTrainingPower2BonusFixed;
            values[2] += card.specialTrainingPower3BonusFixed;
        }
        var lessons = master.GetTable<MasterMasterLesson>("masterLessons").Rows
            .Where(l => l.cardRarityType == card.cardRarityType).OrderBy(l => l.masterRank).ToArray();
        if (owned.masterRank > lessons.Length) throw new InvalidDataException("Master Rank 超出已知参数。");
        foreach (var lesson in lessons.Take(owned.masterRank))
        {
            values[0] += lesson.power1BonusFixed;
            values[1] += lesson.power2BonusFixed;
            values[2] += lesson.power3BonusFixed;
        }
        foreach (var episode in owned.episodes ?? [])
        {
            if (episode.scenarioStatus != "already_read") continue;
            var definition = master.GetTable<MasterCardEpisode>("cardEpisodes", e => e.id).FindById(episode.cardEpisodeId);
            if (definition == null) continue;
            values[0] += definition.power1BonusFixed;
            values[1] += definition.power2BonusFixed;
            values[2] += definition.power3BonusFixed;
        }
        if (user.userMysekaiCanvases?.Any(c => c.cardId == card.id) == true)
        {
            var canvas = master.GetTable<MasterCardMysekaiCanvasBonus>("cardMysekaiCanvasBonuses").Rows
                .FirstOrDefault(c => c.cardRarityType == card.cardRarityType);
            if (canvas != null)
            {
                values[0] += canvas.power1BonusFixed;
                values[1] += canvas.power2BonusFixed;
                values[2] += canvas.power3BonusFixed;
            }
        }
        return values;
    }

    private int GateBonus(SuiteUser user, MasterCard card, string unit, int basic)
    {
        var target = string.IsNullOrEmpty(card.supportUnit) || card.supportUnit == "none" ? unit : card.supportUnit;
        var definition = target == "piapro" ? null : master.GetTable<MasterMysekaiGate>("mysekaiGates").Rows
            .FirstOrDefault(g => g.unit == target);
        var gate = target == "piapro" ? user.userMysekaiGates?.OrderByDescending(g => g.mysekaiGateLevel).FirstOrDefault()
            : user.userMysekaiGates?.FirstOrDefault(g => g.mysekaiGateId == definition?.id);
        if (gate == null) return 0;
        var level = master.GetTable<MasterMysekaiGateLevel>("mysekaiGateLevels").Rows
            .FirstOrDefault(l => l.mysekaiGateId == gate.mysekaiGateId && l.level == gate.mysekaiGateLevel);
        return level == null ? 0 : (int)MathF.Floor(Truncate((float)(level.powerBonusRate / 100.0) * basic, 2));
    }

    private int FixtureBonus(SuiteUser user, int character, int basic, long now)
    {
        var owned = user.userMysekaiFixtureGameCharacterPerformanceBonuses?.FirstOrDefault(b => b.gameCharacterId == character);
        if (owned == null) return 0;
        var displayable = master.GetTable<MasterEvent>("events").Rows
            .FirstOrDefault(e => (e.eventOnlyComponentDisplayStartAt <= now && now < e.startAt)
                || (e.startAt <= now && now < e.aggregateAt));
        var eventLimit = displayable == null ? null : master
            .GetTable<MasterEventMysekaiFixtureGameCharacterPerformanceBonusLimit>("eventMysekaiFixtureGameCharacterPerformanceBonusLimits").Rows
            .FirstOrDefault(l => l.eventId == displayable.id);
        if (fixtureLimit == null && eventLimit == null) return 0;
        var scale = float.Parse(master.GetTable<MasterConfig>("configs").Rows
            .Single(c => c.configKey == "mysekai_fixture_game_character_bonus_rate_scale").value, CultureInfo.InvariantCulture);
        if (scale <= 0) return 0;
        var maximum = Math.Min(fixtureLimit?.bonusRateLimit ?? int.MaxValue, eventLimit?.bonusRateLimit ?? int.MaxValue) / scale;
        maximum = Truncate((float)(Math.Round(maximum * 1000.0, 0, MidpointRounding.AwayFromZero) / 1000.0), 2);
        var rate = (float)(owned.totalBonusRate / scale / 100.0);
        return (int)MathF.Floor(Math.Min(rate, (float)(maximum / 100.0)) * basic);
    }

    private static float Truncate(float value, int digits)
    {
        var scale = Math.Pow(10, digits);
        return (float)(Math.Truncate(value * scale) / scale);
    }

    private static bool Within(long now, long start, long end) => start <= now && (end == 0 ? start != 0 : now < end);
}
