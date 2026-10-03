extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Cards;

public sealed class CardMasterQueries(MasterData master, ResourceMasterQueries resources)
{
    public int[] GetSkillPracticeReleaseConditions()
    {
        var facility = master.GetTable<MasterFacility>("facilities").Rows.Single(f => f.facilityType == "skill_practice");
        return new[] { facility.releaseConditionId, facility.andReleaseConditionId }.Where(id => id > 0).ToArray();
    }

    public MasterLevel[] GetSkillLevels(MasterCard card)
    {
        var type = card.cardRarityType switch
        {
            "rarity_1" => "card_skill_1",
            "rarity_2" => "card_skill_2",
            "rarity_3" => "card_skill_3",
            "rarity_4" => "card_skill_4",
            "rarity_birthday" => "card_skill_birthday",
            _ => throw new InvalidOperationException("Unknown card rarity.")
        };
        var rarity = master.GetTable<MasterCardRarity>("cardRarities").Rows
            .Single(r => r.cardRarityType == card.cardRarityType);
        var levels = master.GetTable<MasterLevel>("levels").Rows
            .Where(l => l.levelType == type && l.level <= rarity.maxSkillLevel)
            .OrderBy(l => l.level).ToArray();
        if (rarity.maxSkillLevel < 1 || levels.Length != rarity.maxSkillLevel ||
            levels[0].level != 1 || levels[0].totalExp != 0)
            throw new InvalidOperationException("Incomplete skill level master data.");
        for (var i = 1; i < levels.Length; i++)
            if (levels[i].level != i + 1 || levels[i].totalExp <= levels[i - 1].totalExp)
                throw new InvalidOperationException("Invalid skill level master data.");
        return levels;
    }

    public int GetSkillPracticeExp(MasterCard card, string type, int id)
    {
        if (type == "skill_practice_ticket")
        {
            var ticket = master.GetTable<MasterSkillPracticeTicket>("skillPracticeTickets", t => t.id)
                .FindById(id) ?? throw new ArgumentException("Unknown skill practice ticket.");
            if (ticket.characterId != 0 && ticket.characterId != card.characterId)
                throw new ArgumentException("Skill practice ticket does not match the character.");
            return ticket.exp;
        }

        var cost = master.GetTable<MasterCardSkillCost>("cardSkillCosts").Rows
            .SingleOrDefault(c => c.materialId == id)
            ?? throw new ArgumentException("Unknown skill practice material.");
        if (cost.characterId != 0 && cost.characterId != card.characterId)
            throw new ArgumentException("Skill practice material does not match the character.");
        if (!string.IsNullOrEmpty(cost.unit) && cost.unit != "none")
        {
            var character = master.GetTable<MasterGameCharacter>("gameCharacters", c => c.id)
                .FindById(card.characterId)
                ?? throw new InvalidOperationException("Missing character master data.");
            if (cost.unit != character.unit)
                throw new ArgumentException("Skill practice material does not match the character unit.");
        }
        return cost.exp;
    }

    public List<int> GetCardEpisodeIds(int cardId)
    {
        var episodes = master.GetTable<MasterCardEpisode>("cardEpisodes", e => e.id).Rows;
        var ids = episodes.Where(e => e.cardId == cardId).Select(e => e.id).ToList();
        while (ids.Count < 2)
            ids.Add(0);
        return ids;
    }

    public MasterCard? GetMasterCard(int cardId) =>
        master.GetTable<MasterCard>("cards", c => c.id).FindById(cardId);

    public MasterCardRarity GetCardRarity(MasterCard card) =>
        master.GetTable<MasterCardRarity>("cardRarities").Rows
            .Single(r => r.cardRarityType == card.cardRarityType);

    public IReadOnlyList<MasterCard> GetMasterCards() =>
        master.GetTable<MasterCard>("cards", c => c.id).Rows;

    public string? GetCardRarityType(int cardId) =>
        GetMasterCard(cardId)?.cardRarityType;

    public int GetCardMaxLevel(int cardId, bool specialTrained)
    {
        var rarityType = GetCardRarityType(cardId);
        if (rarityType == null)
            return 1;

        var rarity = master.GetTable<MasterCardRarity>("cardRarities")
            .Rows
            .FirstOrDefault(r => string.Equals(r.cardRarityType, rarityType, StringComparison.Ordinal));

        if (rarity == null)
            return 1;

        return specialTrained && rarity.trainingMaxLevel > 0
            ? rarity.trainingMaxLevel
            : rarity.maxLevel;
    }

    public int GetPracticeTicketExp(int practiceTicketId) =>
        master.GetTable<MasterPracticeTicket>("practiceTickets", t => t.id)
            .FindById(practiceTicketId)?.exp ?? 0;

    public int GetCardLevelFromTotalExp(int totalExp, int maxLevel)
    {
        var levels = master.GetTable<MasterLevel>("levels")
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

        return master.GetTable<MasterLevel>("levels")
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

        return master.GetTable<MasterMasterLesson>("masterLessons")
            .Rows
            .SelectMany(row => row.costs ?? [])
            .Where(cost => requested.Contains(cost.id))
            .ToArray();
    }

    public MasterMasterLessonReward[] GetMasterLessonRewards(int cardId, int beforeMasterRank, int afterMasterRank)
    {
        if (afterMasterRank <= beforeMasterRank)
            return [];

        return master.GetTable<MasterMasterLessonReward>("masterLessonRewards", r => r.id)
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

        return master.GetTable<MasterCardExchangeResource>("cardExchangeResources")
            .Rows
            .Where(row => string.Equals(row.cardRarityType, cardRarityType, StringComparison.Ordinal))
            .OrderBy(row => row.seq)
            .FirstOrDefault()
            ?.resourceBoxId ?? 0;
    }

    public UserResource[] GetCardExchangeResources(string? cardRarityType)
    {
        var resourceBoxId = GetCardExchangeResourceBoxId(cardRarityType);
        return resources.BuildResourcesFromBox("card_exchange_resource", resourceBoxId);
    }

    public int[] GetCardCostume3dIds(int cardId) =>
        master.GetTable<MasterCardCostume3D>("cardCostume3ds")
            .Rows
            .Where(c => c.cardId == cardId && c.costume3dId > 0)
            .Select(c => c.costume3dId)
            .ToArray();
}
