extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;
using UserMasterLessonReward = game::Sekai.UserCardMasterLessonResponse.UserMasterLessonReward;

namespace PrivateSekai.Modules.Cards;

public sealed class CardService(
    UserSession user,
    CardMasterQueries master,
    ResourceMasterQueries resourceMaster,
    ResourceService resourceService,
    MissionService missions)
{
    public UpdateExpResult PracticeCardSkill(int cardId, UserResource[]? costs, string resourceType)
    {
        if (resourceType is not ("material" or "skill_practice_ticket") || costs == null || costs.Length == 0)
            throw new ArgumentException("Missing skill practice costs.");
        var card = user.Data.userCards?.SingleOrDefault(c => c.cardId == cardId)
            ?? throw new ArgumentException("Card is not owned.");
        var masterCard = master.GetMasterCard(cardId)
            ?? throw new InvalidOperationException("Missing card master data.");
        var levels = master.GetSkillLevels(masterCard);
        var beforeTotal = card.totalSkillExp;
        if (beforeTotal < 0 || beforeTotal >= levels[^1].totalExp)
            throw new ArgumentException("Card skill experience is invalid or already at maximum.");

        var quantities = new Dictionary<int, int>();
        foreach (var cost in costs)
        {
            if (cost == null || cost.resourceType != resourceType || cost.resourceId <= 0 || cost.quantity <= 0)
                throw new ArgumentException("Invalid skill practice cost.");
            quantities[cost.resourceId] = checked(quantities.GetValueOrDefault(cost.resourceId) + cost.quantity);
        }

        long addedExp = 0;
        foreach (var (id, quantity) in quantities)
        {
            var exp = master.GetSkillPracticeExp(masterCard, resourceType, id);
            if (exp <= 0)
                throw new InvalidOperationException("Invalid skill practice experience in master data.");
            var owned = resourceType == "material"
                ? user.Data.userMaterials?.SingleOrDefault(m => m.materialId == id)?.quantity ?? 0
                : user.Data.userSkillPracticeTickets?.SingleOrDefault(t => t.skillPracticeTicketId == id)?.quantity ?? 0;
            if (owned < quantity)
                throw new ArgumentException("Insufficient skill practice resources.");
            addedExp = checked(addedExp + (long)exp * quantity);
        }

        var afterTotal = (int)Math.Min(levels[^1].totalExp, checked(beforeTotal + addedExp));
        var before = levels.Last(l => l.totalExp <= beforeTotal);
        var after = levels.Last(l => l.totalExp <= afterTotal);
        foreach (var (id, quantity) in quantities)
            resourceService.Consume(resourceType, id, quantity);
        card.totalSkillExp = afterTotal;
        card.skillLevel = after.level;
        card.skillExp = afterTotal - after.totalExp;
        user.MarkChanged(nameof(SuiteUser.userCards));

        return new UpdateExpResult
        {
            beforeTotalExp = beforeTotal, afterTotalExp = afterTotal,
            beforeLevel = before.level, afterLevel = after.level,
            beforeExp = beforeTotal - before.totalExp, afterExp = card.skillExp
        };
    }

    public void ExchangeCards(UserCard[]? userCards)
    {
        if (userCards == null || userCards.Length == 0)
            return;

        user.Data.userCards ??= [];
        var cards = user.Data.userCards;
        foreach (var requestCard in userCards.Where(c => c.cardId > 0))
        {
            var current = cards.FirstOrDefault(c => c.cardId == requestCard.cardId);
            if (current == null)
                continue;

            var targetDuplicateCount = Math.Max(0, requestCard.duplicateCount);
            var exchangeCount = current.duplicateCount - targetDuplicateCount;
            if (exchangeCount <= 0)
                continue;

            current.duplicateCount = targetDuplicateCount;

            var exchangeResources = master.GetCardExchangeResources(master.GetCardRarityType(current.cardId));
            for (var i = 0; i < exchangeCount; i++)
            {
                foreach (var resource in exchangeResources)
                    resourceService.Grant(resource);
            }
        }

        user.Data.userCards = cards.OrderBy(c => c.cardId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userCards));
    }

    public UserCardPracticeTicketResponse PracticeCardWithTickets(int cardId, UserResource[]? costs)
    {
        var card = FindOrCreateUserCard(cardId);
        var addExp = 0;
        if (costs != null)
        {
            foreach (var cost in costs)
            {
                if (!string.Equals(cost.resourceType, "practice_ticket", StringComparison.Ordinal) ||
                    cost.resourceId <= 0 ||
                    cost.quantity <= 0)
                    continue;

                resourceService.Consume("practice_ticket", cost.resourceId, cost.quantity);
                addExp += master.GetPracticeTicketExp(cost.resourceId) * cost.quantity;
            }
        }

        var result = AddCardExperience(card, addExp);
        if (result.afterLevel > result.beforeLevel)
            missions.RecordCardPracticeLevelUp(result.afterLevel - result.beforeLevel);
        return new UserCardPracticeTicketResponse { updateExpResult = result };
    }

    public UpdateExpResult GainExperience(int cardId, int addExp)
    {
        if (addExp < 0) throw new ArgumentOutOfRangeException(nameof(addExp));
        var card = user.Data.userCards?.SingleOrDefault(c => c.cardId == cardId)
            ?? throw new ArgumentException("Card is not owned.");
        return AddCardExperience(card, addExp);
    }

    private UpdateExpResult AddCardExperience(UserCard card, int addExp)
    {
        var beforeTotalExp = card.totalExp;
        var beforeLevel = card.level;
        var beforeExp = Math.Max(0, beforeTotalExp - master.GetCardLevelTotalExp(beforeLevel));
        var maxLevel = master.GetCardMaxLevel(card.cardId, string.Equals(card.specialTrainingStatus, "done", StringComparison.Ordinal));
        var maxTotalExp = master.GetCardLevelMaxTotalExp(maxLevel);
        var afterTotalExp = maxTotalExp > 0
            ? Math.Min(maxTotalExp, beforeTotalExp + addExp)
            : beforeTotalExp + addExp;
        var afterLevel = master.GetCardLevelFromTotalExp(afterTotalExp, maxLevel);
        var afterExp = Math.Max(0, afterTotalExp - master.GetCardLevelTotalExp(afterLevel));

        card.totalExp = afterTotalExp;
        card.level = afterLevel;
        card.exp = afterExp;
        user.MarkChanged(nameof(SuiteUser.userCards));
        return new UpdateExpResult
        {
            beforeTotalExp = beforeTotalExp, afterTotalExp = afterTotalExp,
            beforeExp = beforeExp, afterExp = afterExp,
            beforeLevel = beforeLevel, afterLevel = afterLevel
        };
    }

    public UserCardMasterLessonResponse MasterLessonCard(int cardId, IEnumerable<int>? masterLessonCostIds)
    {
        var card = FindOrCreateUserCard(cardId);
        var beforeMasterRank = card.masterRank;

        foreach (var cost in master.GetMasterLessonCosts(masterLessonCostIds))
            resourceService.Consume(cost.resourceType, cost.resourceId, cost.quantity);

        var requestedCount = masterLessonCostIds?.Count(id => id > 0) ?? 0;
        card.masterRank = Math.Clamp(card.masterRank + requestedCount, 0, 5);
        user.MarkChanged(nameof(SuiteUser.userCards));

        var obtainedRewards = new List<UserMasterLessonReward>();
        foreach (var reward in master.GetMasterLessonRewards(cardId, beforeMasterRank, card.masterRank))
        {
            var resources = resourceMaster.BuildResourcesFromBox("master_lesson_reward", reward.resourceBoxId);
            foreach (var resource in resources)
                resourceService.Grant(resource);

            obtainedRewards.Add(new UserMasterLessonReward
            {
                masterLessonRewardId = reward.id,
                obtainRewards = resources
            });
        }

        return new UserCardMasterLessonResponse
        {
            obtainedRewards = obtainedRewards.ToArray()
        };
    }

    public void SetCardSpecialTrainingStatus(int cardId, string? specialTrainingStatus)
    {
        if (specialTrainingStatus != "done")
            throw new ArgumentException("Invalid special training status.");
        var card = user.Data.userCards?.SingleOrDefault(c => c.cardId == cardId)
            ?? throw new ArgumentException("Card is not owned.");
        if (card.specialTrainingStatus == "done")
            return;
        var masterCard = master.GetMasterCard(cardId)
            ?? throw new InvalidOperationException("Missing card master data.");
        var rarity = master.GetCardRarity(masterCard);
        if (masterCard.specialTrainingPower1BonusFixed == 0 &&
            masterCard.specialTrainingPower2BonusFixed == 0 && masterCard.specialTrainingPower3BonusFixed == 0)
            throw new ArgumentException("Card does not support special training.");
        if (rarity.maxLevel <= 0 || rarity.trainingMaxLevel <= rarity.maxLevel)
            throw new InvalidOperationException("Invalid special training level limits.");
        if (card.level < rarity.maxLevel)
            throw new ArgumentException("Card has not reached the required level.");
        var costs = masterCard.specialTrainingCosts;
        if (costs == null || costs.Length == 0)
            throw new InvalidOperationException("Missing special training costs.");
        var quantities = new Dictionary<int, int>();
        foreach (var entry in costs)
        {
            var cost = entry.cost;
            if (entry.cardId != cardId || cost == null || cost.resourceType != "material" ||
                cost.resourceId <= 0 || cost.quantity <= 0)
                throw new InvalidOperationException("Unsupported or invalid special training cost.");
            quantities[cost.resourceId] = checked(quantities.GetValueOrDefault(cost.resourceId) + cost.quantity);
        }
        foreach (var (id, quantity) in quantities)
            if ((user.Data.userMaterials?.SingleOrDefault(m => m.materialId == id)?.quantity ?? 0) < quantity)
                throw new ArgumentException("Insufficient special training materials.");

        var rewards = resourceMaster.BuildResourcesFromBox("special_training_reward", masterCard.specialTrainingRewardResourceBoxId);
        if (masterCard.specialTrainingRewardResourceBoxId > 0 && rewards.Length == 0)
            throw new InvalidOperationException("Missing special training reward box.");
        foreach (var (id, quantity) in quantities)
            resourceService.Consume("material", id, quantity);
        resourceService.Grant(rewards);
        card.specialTrainingStatus = "done";
        user.MarkChanged(nameof(SuiteUser.userCards));
    }

    public void SetCardDefaultImage(int cardId, string? defaultImage)
    {
        var card = FindOrCreateUserCard(cardId);
        if (!string.IsNullOrWhiteSpace(defaultImage))
            card.defaultImage = defaultImage;

        user.MarkChanged(nameof(SuiteUser.userCards));
    }

    private UserCard FindOrCreateUserCard(int cardId)
    {
        var card = user.Data.userCards?.FirstOrDefault(c => c.cardId == cardId);
        if (card != null)
            return card;

        resourceService.Grant(new UserResource { resourceType = "card", resourceId = cardId, quantity = 1 });
        return user.Data.userCards!.First(c => c.cardId == cardId);
    }
}
