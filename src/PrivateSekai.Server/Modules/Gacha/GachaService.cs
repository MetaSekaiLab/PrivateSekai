extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Gacha;

public sealed class GachaService(
    UserSession user,
    GachaMasterQueries master,
    CardMasterQueries cards,
    ResourceMasterQueries resourceMaster,
    ResourceService resources)
{
    public UserGachaResponse ExecuteGacha(int gachaId, int gachaBehaviorId, bool isPriorityUsePaidJewel)
    {
        var now = user.Now;
        var gacha = master.GetMasterGacha(gachaId);
        var behavior = master.GetMasterGachaBehavior(gacha, gachaBehaviorId);
        var spinCount = GetGachaSpinCount(behavior);
        var behaviorType = behavior?.gachaBehaviorType;

        var consumedCosts = ConsumeGachaCost(behavior, spinCount, isPriorityUsePaidJewel);

        var drawnCardIds = DrawGachaCards(gacha, behaviorType, spinCount);
        var obtainPrizes = new List<UserGachaSpinObtainPrize>();
        foreach (var cardId in drawnCardIds)
        {
            var isNew = GrantGachaCard(cardId);
            var costume3ds = isNew ? GrantInitialCostumes(cardId) : [];
            obtainPrizes.Add(new UserGachaSpinObtainPrize
            {
                card = new UserResource
                {
                    resourceId = cardId,
                    resourceType = "card",
                    resourceLevel = 1,
                    quantity = 1
                },
                newFlg = isNew,
                gachaLotteryType = "normal",
                costume3d = costume3ds,
                cardExtra = []
            });
        }

        var userGacha = UpsertUserGacha(gachaId, gachaBehaviorId, now);
        var obtainGachaCeilItems = GrantGachaCeilItem(gacha, gachaId, spinCount);

        user.MarkChanged(new []
        {
            nameof(SuiteUser.userCharacterMissions),
            nameof(SuiteUser.userCharacterMissionStatuses),
            nameof(SuiteUser.userHonorMissions)
        });

        return new UserGachaResponse
        {
            consumedCosts = consumedCosts,
            obtainPrizes = obtainPrizes.ToArray(),
            obtainGachaCeilItems = obtainGachaCeilItems,
            obtainGachaBonusItems = [],
            obtainGachaExtras = [],
            obtainGachaFreebies = [],
            userGacha = userGacha,
            obtainCharacterAllBonuses = [],
            obtainCharacterRepeatedBonuses = []
        };
    }

    public void SaveRateChoiceGachaWish(UserRateChoiceGachaWishRequest request)
    {
        if (request.rateChoiceGachaDetails == null)
            return;

        user.Data.userRateChoiceGachaWishes ??= [];
        var wishes = user.Data.userRateChoiceGachaWishes
            .Where(w => w.gachaId != request.gachaId)
            .ToList();

        wishes.AddRange(request.rateChoiceGachaDetails.Select(detail => new UserRateChoiceGachaWish
        {
            gachaId = request.gachaId,
            gachaDetailId = detail.gachaDetailId,
            rateChoiceGachaWishId = detail.rateChoiceGachaWishId
        }));

        user.Data.userRateChoiceGachaWishes = wishes
            .OrderBy(w => w.gachaId)
            .ThenBy(w => w.rateChoiceGachaWishId)
            .ThenBy(w => w.gachaDetailId)
            .ToArray();
        user.MarkChanged(nameof(SuiteUser.userRateChoiceGachaWishes));
    }

    public UserGachaCeilExchangeResponse ExchangeGachaCeilItem(UserGachaCeilExchangeRequest request)
    {
        var exchangeRequest = request.gachaCeilExchangeRequest;
        if (exchangeRequest == null || exchangeRequest.gachaExchangeId <= 0 || exchangeRequest.exchangeCount <= 0)
        {
            return new UserGachaCeilExchangeResponse
            {
                obtainUserResources = []
            };
        }

        var exchange = master.GetMasterGachaCeilExchange(exchangeRequest.gachaExchangeId);
        var resourceBoxId = exchange?.resourceBoxId ?? 0;
        var obtainResources = resourceMaster.BuildResourcesFromBox("gacha_ceil_exchange", resourceBoxId);

        var exchangeCount = exchangeRequest.exchangeCount;
        ConsumeGachaCeilExchangeCost(exchange?.gachaCeilExchangeCost, exchangeCount);
        ConsumeGachaCeilSubstituteCost(exchange, exchangeRequest);

        var exchangeId = exchangeRequest.gachaExchangeId;
        var exchangeLimit = exchange?.exchangeLimit ?? 0;
        UpsertUserGachaCeilExchange(exchangeId, exchangeLimit, exchangeCount);

        var obtained = new Dictionary<(string? Type, int Id, int Level), UserResource>();
        for (var i = 0; i < exchangeCount; i++)
        {
            foreach (var resource in obtainResources)
            {
                resources.Grant(resource);
                AddObtainedResource(obtained, resource);
            }
        }

        return new UserGachaCeilExchangeResponse
        {
            obtainUserResources = obtained.Values.ToArray()
        };
    }

    private static int GetGachaSpinCount(MasterGachaBehavior? behavior)
    {
        var spinCount = behavior?.spinCount ?? 0;
        if (spinCount > 0)
            return spinCount;

        return 1;
    }

    private UserResource[] ConsumeGachaCost(MasterGachaBehavior? behavior, int spinCount, bool isPriorityUsePaidJewel)
    {
        if (string.Equals(behavior?.resourceCategory, "free_resource", StringComparison.Ordinal))
            return [];

        var costResourceType = behavior?.costResourceType;
        var costResourceId = behavior?.costResourceId ?? 0;
        var costQuantity = behavior?.costResourceQuantity ?? 300 * spinCount;
        if (costQuantity <= 0 || string.IsNullOrEmpty(costResourceType))
            return [];

        return costResourceType switch
        {
            "jewel" =>
            [
                new UserResource
                {
                    resourceType = "jewel",
                    resourceLevel = 0,
                    quantity = resources.Consume("jewel", 0, costQuantity, isPriorityUsePaidJewel)
                }
            ],
            "paid_jewel" =>
            [
                new UserResource
                {
                    resourceType = "paid_jewel",
                    resourceLevel = 0,
                    quantity = resources.Consume("paid_jewel", 0, costQuantity)
                }
            ],
            "gacha_ticket" =>
            [
                new UserResource
                {
                    resourceId = costResourceId,
                    resourceType = "gacha_ticket",
                    resourceLevel = 1,
                    quantity = resources.Consume("gacha_ticket", costResourceId, costQuantity)
                }
            ],
            _ => []
        };
    }

    private int[] DrawGachaCards(MasterGacha? gacha, string? behaviorType, int spinCount)
    {
        var cardIds = new int[spinCount];
        for (var i = 0; i < cardIds.Length; i++)
            cardIds[i] = DrawGachaCard(gacha);

        if (cardIds.Length == 0)
            return cardIds;

        if (string.Equals(behaviorType, "over_rarity_4_once", StringComparison.Ordinal) &&
            !cardIds.Any(IsRarity4OrHigher))
        {
            cardIds[^1] = DrawGachaCard(gacha, IsRarity4OrHigher);
        }
        else if (string.Equals(behaviorType, "over_rarity_3_once", StringComparison.Ordinal) &&
                 !cardIds.Any(IsRarity3OrHigher))
        {
            cardIds[^1] = DrawGachaCard(gacha, IsRarity3OrHigher);
        }

        return cardIds;
    }

    private int DrawGachaCard(MasterGacha? gacha, Func<int, bool>? cardFilter = null)
    {
        var rarityType = DrawGachaRarity(gacha, cardFilter);
        return DrawGachaCardByRarity(gacha, rarityType, cardFilter);
    }

    private string? DrawGachaRarity(MasterGacha? gacha, Func<int, bool>? cardFilter)
    {
        if (gacha?.gachaCardRarityRates == null)
            return null;

        var candidates = gacha.gachaCardRarityRates
            .Where(rate => rate.lotteryType == "normal")
            .Select(rate => new
            {
                RarityType = rate.cardRarityType,
                Rate = rate.rate
            })
            .Where(rate => !string.IsNullOrEmpty(rate.RarityType) &&
                           rate.Rate > 0 &&
                           HasGachaCardInRarity(gacha, rate.RarityType!, cardFilter))
            .ToArray();

        var totalRate = candidates.Sum(rate => rate.Rate);
        if (totalRate <= 0)
            return null;

        var selected = Random.Shared.NextDouble() * totalRate;
        double running = 0;
        foreach (var candidate in candidates)
        {
            running += candidate.Rate;
            if (selected < running)
                return candidate.RarityType;
        }

        return candidates[^1].RarityType;
    }

    private bool HasGachaCardInRarity(MasterGacha? gacha, string rarityType, Func<int, bool>? cardFilter)
    {
        if (gacha?.gachaDetails == null)
            return false;

        foreach (var detail in gacha.gachaDetails)
        {
            if (detail.cardId <= 0 || cardFilter?.Invoke(detail.cardId) == false)
                continue;

            if (string.Equals(cards.GetCardRarityType(detail.cardId), rarityType, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private int DrawGachaCardByRarity(MasterGacha? gacha, string? rarityType, Func<int, bool>? cardFilter)
    {
        if (gacha?.gachaDetails == null)
            return DrawFallbackCard(cardFilter);

        var candidates = gacha.gachaDetails
            .Select(detail => new
            {
                CardId = detail.cardId,
                Weight = detail.weight
            })
            .Where(detail => detail.CardId > 0 && detail.Weight > 0)
            .Where(detail => cardFilter?.Invoke(detail.CardId) != false)
            .Where(detail => string.IsNullOrEmpty(rarityType) ||
                             string.Equals(cards.GetCardRarityType(detail.CardId), rarityType, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
            return DrawFallbackCard(cardFilter);

        var totalWeight = candidates.Sum(detail => (long)detail.Weight);
        if (totalWeight <= 0)
            return DrawFallbackCard(cardFilter);

        var selected = Random.Shared.NextInt64(totalWeight);
        long running = 0;
        foreach (var candidate in candidates)
        {
            running += candidate.Weight;
            if (selected < running)
                return candidate.CardId;
        }

        return DrawFallbackCard(cardFilter);
    }

    private int DrawFallbackCard(Func<int, bool>? cardFilter)
    {
        var candidates = cards.GetMasterCards()
            .Select(card => card.id)
            .Where(cardId => cardId > 0 && !string.Equals(cards.GetCardRarityType(cardId), "rarity_1", StringComparison.Ordinal))
            .Where(cardId => cardFilter?.Invoke(cardId) != false)
            .ToArray();

        return candidates.Length == 0 ? 1 : candidates[Random.Shared.Next(candidates.Length)];
    }

    private bool IsRarity3OrHigher(int cardId) =>
        RarityRank(cards.GetCardRarityType(cardId)) >= 3;

    private bool IsRarity4OrHigher(int cardId) =>
        RarityRank(cards.GetCardRarityType(cardId)) >= 4;

    private static int RarityRank(string? rarityType) =>
        rarityType switch
        {
            "rarity_birthday" => 4,
            "rarity_4" => 4,
            "rarity_3" => 3,
            "rarity_2" => 2,
            "rarity_1" => 1,
            _ => 0
        };

    private UserGacha UpsertUserGacha(int gachaId, int gachaBehaviorId, long now)
    {
        user.Data.userGachas ??= [];
        var gachas = user.Data.userGachas.ToList();
        var userGacha = gachas.FirstOrDefault(g =>
            g.gachaId == gachaId &&
            g.gachaBehaviorId == gachaBehaviorId);

        if (userGacha == null)
        {
            userGacha = new UserGacha
            {
                userId = user.UserId,
                gachaId = gachaId,
                gachaBehaviorId = gachaBehaviorId
            };
            gachas.Add(userGacha);
        }

        userGacha.count++;
        userGacha.lastSpinAt = now;
        user.Data.userGachas = gachas.ToArray();
        user.MarkChanged(nameof(SuiteUser.userGachas));
        return userGacha;
    }

    private void ConsumeGachaCeilExchangeCost(MasterGachaCeilExchangeCost? cost, int exchangeCount)
    {
        if (cost == null || exchangeCount <= 0)
            return;

        var resourceId = cost.resourceId > 0 ? cost.resourceId : cost.gachaCeilItemId;
        var quantity = cost.quantity * exchangeCount;
        ConsumeExchangeResource(cost.resourceType, resourceId, quantity);
    }

    private void ConsumeGachaCeilSubstituteCost(MasterGachaCeilExchange? exchange, UserGachaCeilItemExchangeRequest request)
    {
        if (exchange?.substituteCosts == null ||
            request.gachaCeilExchangeSubstituteCostId <= 0 ||
            request.substituteCostCount <= 0)
        {
            return;
        }

        foreach (var entry in exchange.substituteCosts)
        {
            if (entry.id != request.gachaCeilExchangeSubstituteCostId)
                continue;

            var quantity = entry.substituteQuantity * request.substituteCostCount;
            ConsumeExchangeResource(entry.resourceType, entry.resourceId, quantity);
            UpsertUserGachaCeilExchangeSubstituteCost(request.gachaExchangeId, request.substituteCostCount);
            return;
        }
    }

    private void UpsertUserGachaCeilExchange(int gachaCeilExchangeId, int exchangeLimit, int exchangeCount)
    {
        user.Data.userGachaCeilExchanges ??= [];
        var exchanges = user.Data.userGachaCeilExchanges.ToList();
        var userExchange = exchanges.FirstOrDefault(e => e.gachaCeilExchangeId == gachaCeilExchangeId);
        if (userExchange == null)
        {
            userExchange = new UserGachaCeilExchange
            {
                userId = user.UserId,
                gachaCeilExchangeId = gachaCeilExchangeId,
                exchangeStatus = "exchangeable",
                exchangeRemaining = exchangeLimit
            };
            exchanges.Add(userExchange);
        }

        if (exchangeLimit > 0)
        {
            userExchange.exchangeRemaining = Math.Max(0, userExchange.exchangeRemaining - exchangeCount);
            userExchange.exchangeStatus = userExchange.exchangeRemaining == 0 ? "not_exchangeable" : "exchangeable";
        }

        user.Data.userGachaCeilExchanges = exchanges.OrderBy(e => e.gachaCeilExchangeId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userGachaCeilExchanges));
    }

    private void UpsertUserGachaCeilExchangeSubstituteCost(int gachaCeilExchangeId, int usedCount)
    {
        user.Data.userGachaCeilExchangeSubstituteCosts ??= [];
        var costs = user.Data.userGachaCeilExchangeSubstituteCosts.ToList();
        var cost = costs.FirstOrDefault(c => c.gachaCeilExchangeId == gachaCeilExchangeId);
        if (cost == null)
        {
            cost = new UserGachaCeilExchangeSubstituteCost
            {
                gachaCeilExchangeId = gachaCeilExchangeId
            };
            costs.Add(cost);
        }

        cost.substituteCostUsedCount += usedCount;
        user.Data.userGachaCeilExchangeSubstituteCosts = costs.OrderBy(c => c.gachaCeilExchangeId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userGachaCeilExchangeSubstituteCosts));
    }

    private static void AddObtainedResource(
        IDictionary<(string? Type, int Id, int Level), UserResource> obtained,
        UserResource resource)
    {
        var key = (resource.resourceType, resource.resourceId, resource.resourceLevel);
        if (obtained.TryGetValue(key, out var current))
        {
            current.quantity += resource.quantity;
            return;
        }

        obtained[key] = new UserResource
        {
            resourceType = resource.resourceType,
            resourceId = resource.resourceId,
            resourceLevel = resource.resourceLevel,
            quantity = resource.quantity
        };
    }

    private bool GrantGachaCard(int cardId)
    {
        var isNew = user.Data.userCards?.All(card => card.cardId != cardId) ?? true;
        resources.Grant(new UserResource
        {
            resourceType = "card",
            resourceId = cardId,
            resourceLevel = 1,
            quantity = 1
        });
        return isNew;
    }

    private UserResource[] GrantInitialCostumes(int cardId)
    {
        var owned = new HashSet<int>(user.Data.userCostume3dStatuses?.Select(s => s.costume3dId) ?? []);
        var obtained = new List<UserResource>();
        foreach (var costumeId in cards.GetCardCostume3dIds(cardId))
        {
            if (!owned.Add(costumeId))
                continue;

            var resource = new UserResource
            {
                resourceId = costumeId,
                resourceType = "costume_3d",
                resourceLevel = 0,
                quantity = 1
            };
            resources.Grant(resource);
            obtained.Add(resource);
        }
        return obtained.ToArray();
    }

    private UserResource[] GrantGachaCeilItem(MasterGacha? gacha, int gachaId, int quantity)
    {
        var ceilItemId = master.ResolveGachaCeilItemId(gacha, gachaId);
        if (ceilItemId <= 0 || quantity <= 0)
            return [];

        var reward = new UserResource
        {
            resourceId = ceilItemId,
            resourceType = "gacha_ceil_item",
            resourceLevel = 0,
            quantity = quantity
        };
        resources.Grant(reward);
        return [reward];
    }

    private void ConsumeExchangeResource(string? type, int id, int quantity)
    {
        if (quantity <= 0)
            return;

        if (type is "material" or "coin" or "jewel" or "paid_jewel"
            or "gacha_ceil_item" or "gacha_ticket" or "practice_ticket")
            resources.Consume(type, id, quantity);
    }
}
