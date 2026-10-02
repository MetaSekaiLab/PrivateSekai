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
        var beforeTotalExp = card.totalExp;
        var beforeLevel = card.level;
        var beforeExp = Math.Max(0, beforeTotalExp - master.GetCardLevelTotalExp(beforeLevel));

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

        var maxLevel = master.GetCardMaxLevel(cardId, string.Equals(card.specialTrainingStatus, "done", StringComparison.Ordinal));
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
        if (afterLevel > beforeLevel)
            missions.TouchBeginnerMissionProgress(6);

        return new UserCardPracticeTicketResponse
        {
            updateExpResult = new UpdateExpResult
            {
                beforeTotalExp = beforeTotalExp,
                afterTotalExp = afterTotalExp,
                beforeExp = beforeExp,
                afterExp = afterExp,
                beforeLevel = beforeLevel,
                afterLevel = afterLevel
            }
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
        var card = FindOrCreateUserCard(cardId);
        card.specialTrainingStatus = string.IsNullOrWhiteSpace(specialTrainingStatus)
            ? card.specialTrainingStatus
            : specialTrainingStatus;

        if (string.Equals(card.specialTrainingStatus, "done", StringComparison.Ordinal))
        {
            var maxLevel = master.GetCardMaxLevel(cardId, true);
            card.level = Math.Min(card.level, maxLevel);
        }

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
