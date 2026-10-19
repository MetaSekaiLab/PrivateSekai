extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Gacha;

public sealed class GachaService(
    UserSession user,
    GachaMasterQueries master,
    CardMasterQueries cards,
    ResourceMasterQueries resourceMaster,
    ResourceService resources,
    MissionService missions,
    Random? random = null)
{
    public UserGachaResponse ExecuteGacha(int gachaId, int gachaBehaviorId, bool isPriorityUsePaidJewel)
    {
        var now = user.Now;
        var gacha = master.GetMasterGacha(gachaId) ?? throw new GachaRequestRejectedException(404);
        var behavior = master.GetMasterGachaBehavior(gacha, gachaBehaviorId) ?? throw new GachaRequestRejectedException(404);
        if (gacha.startAt > now || (gacha.endAt == 0 ? gacha.startAt == 0 : now >= gacha.endAt))
            throw new GachaRequestRejectedException(409, "invalid_term");
        if (behavior.gachaSpinnableType == "colorful_pass" &&
            (user.Data.userColorfulPassV2 == null || user.Data.userColorfulPassV2.expiredAt < (ulong)now))
            throw new GachaRequestRejectedException(409);
        var spinCount = GetGachaSpinCount(behavior);
        var behaviorType = behavior?.gachaBehaviorType;

        var consumedCosts = ConsumeGachaCost(behavior, isPriorityUsePaidJewel);

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
        missions.RefreshGachaPanels();

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
        var exchangeRequest = request.gachaCeilExchangeRequest ?? throw new GachaRequestRejectedException(500);
        if (exchangeRequest.exchangeCount <= 0) throw new GachaRequestRejectedException(400);
        var found = master.FindGachaCeilExchange(exchangeRequest.gachaExchangeId) ?? throw new GachaRequestRejectedException(404);
        var exchange = found.Exchange;
        if (!WithinPeriod(found.Summary.startAt, found.Summary.endAt) || !WithinPeriod(exchange.startAt, exchange.endAt))
            throw new GachaRequestRejectedException(409, "invalid_term");
        var exchangeCount = exchangeRequest.exchangeCount;
        var saved = user.Data.userGachaCeilExchanges?.SingleOrDefault(e => e.gachaCeilExchangeId == exchange.id);
        if ((saved != null && saved.exchangeStatus != "exchangeable") ||
            (exchange.exchangeLimit > 0 && exchangeCount > (saved?.exchangeRemaining ?? exchange.exchangeLimit)))
            throw new GachaRequestRejectedException(409);
        var costs = BuildGachaCeilCosts(exchange, exchangeRequest);
        foreach (var cost in costs)
            if (GetExchangeBalance(cost.resourceType!, cost.resourceId) < cost.quantity)
                throw new GachaRequestRejectedException(409, "not_enough_resource");
        var obtainResources = resourceMaster.BuildResourcesFromBox("gacha_ceil_exchange", exchange.resourceBoxId);
        if (obtainResources.Length == 0) throw new InvalidOperationException("Missing gacha exchange rewards.");
        foreach (var cost in costs) resources.Consume(cost.resourceType, cost.resourceId, cost.quantity);
        if (exchangeRequest.substituteCostCount > 0)
            UpsertUserGachaCeilExchangeSubstituteCost(exchange.id, exchangeRequest.substituteCostCount);
        UpsertUserGachaCeilExchange(exchange.id, exchange.exchangeLimit, exchangeCount);
        var obtained = new Dictionary<(string? Type, int Id, int Level), UserResource>();
        foreach (var resource in obtainResources)
        {
            var reward = new UserResource
            {
                resourceType = resource.resourceType, resourceId = resource.resourceId, resourceLevel = resource.resourceLevel,
                quantity = checked(resource.quantity * exchangeCount)
            };
            resources.Grant(reward);
            AddObtainedResource(obtained, reward);
        }

        return new UserGachaCeilExchangeResponse
        {
            obtainUserResources = obtained.Values.ToArray()
        };
    }

    private bool WithinPeriod(long start, long end) => start <= user.Now && (end == 0 ? start != 0 : user.Now < end);

    private static int GetGachaSpinCount(MasterGachaBehavior? behavior)
    {
        var spinCount = behavior?.spinCount ?? 0;
        if (spinCount > 0)
            return spinCount;

        throw new InvalidOperationException("Missing gacha spin count.");
    }

    private UserResource[] ConsumeGachaCost(MasterGachaBehavior? behavior, bool isPriorityUsePaidJewel)
    {
        if (string.Equals(behavior?.resourceCategory, "free_resource", StringComparison.Ordinal))
            return [];

        var costResourceType = behavior?.costResourceType;
        var costResourceId = behavior?.costResourceId ?? 0;
        var costQuantity = behavior?.costResourceQuantity ?? 0;
        if (costQuantity <= 0 || string.IsNullOrEmpty(costResourceType))
            throw new InvalidOperationException("Missing gacha cost.");

        var currency = user.Data.userChargedCurrency;
        var available = costResourceType switch
        {
            "jewel" => (long)(currency?.free ?? 0) + (currency?.paid ?? 0),
            "paid_jewel" => currency?.paid ?? 0,
            "gacha_ticket" => user.Data.userGachaTickets?.SingleOrDefault(t => t.gachaTicketId == costResourceId)?.quantity ?? 0,
            _ => throw new NotSupportedException("Unsupported gacha cost.")
        };
        if (available < costQuantity) throw new GachaRequestRejectedException(409, "not_enough_resource");

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
            _ => throw new NotSupportedException("Unsupported gacha cost.")
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
            throw new InvalidOperationException("Missing gacha rarity rates.");

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
            throw new InvalidOperationException("No eligible gacha rarity rates.");

        var selected = (random ?? Random.Shared).NextDouble() * totalRate;
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
            throw new InvalidOperationException("Missing gacha card pool.");

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
            throw new InvalidOperationException("No eligible gacha cards.");

        var totalWeight = candidates.Sum(detail => (long)detail.Weight);
        if (totalWeight <= 0)
            throw new InvalidOperationException("Invalid gacha card weights.");

        var selected = (random ?? Random.Shared).NextInt64(totalWeight);
        long running = 0;
        foreach (var candidate in candidates)
        {
            running += candidate.Weight;
            if (selected < running)
                return candidate.CardId;
        }

        throw new InvalidOperationException("Gacha card selection failed.");
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

    private UserResource[] BuildGachaCeilCosts(MasterGachaCeilExchange exchange, UserGachaCeilItemExchangeRequest request)
    {
        var cost = exchange.gachaCeilExchangeCost ?? throw new InvalidOperationException("Missing gacha exchange cost.");
        var resourceId = cost.resourceId > 0 ? cost.resourceId : cost.gachaCeilItemId;
        if (cost.quantity <= 0 || resourceId <= 0 || string.IsNullOrEmpty(cost.resourceType))
            throw new InvalidOperationException("Invalid gacha exchange cost.");
        var primary = new UserResource { resourceType = cost.resourceType, resourceId = resourceId, quantity = checked(cost.quantity * request.exchangeCount) };
        var costs = new List<UserResource> { primary };
        if (request.substituteCostCount < 0) throw new GachaRequestRejectedException(400);
        if (request.substituteCostCount > 0)
        {
            var substitute = exchange.substituteCosts?.SingleOrDefault(c => c.id == request.gachaCeilExchangeSubstituteCostId)
                ?? throw new GachaRequestRejectedException(400);
            var used = user.Data.userGachaCeilExchangeSubstituteCosts?.SingleOrDefault(c => c.gachaCeilExchangeId == exchange.id)?.substituteCostUsedCount ?? 0;
            if (request.substituteCostCount > exchange.substituteLimit - used) throw new GachaRequestRejectedException(409);
            primary.quantity = checked(primary.quantity - substitute.substituteQuantity * request.substituteCostCount);
            if (primary.quantity < 0) throw new GachaRequestRejectedException(400);
            costs.Add(new UserResource { resourceType = substitute.resourceType, resourceId = substitute.resourceId, quantity = request.substituteCostCount });
        }
        return costs.GroupBy(c => (c.resourceType, c.resourceId)).Select(g => new UserResource
            { resourceType = g.Key.resourceType, resourceId = g.Key.resourceId, quantity = g.Sum(c => c.quantity) }).ToArray();
    }

    private long GetExchangeBalance(string type, int id) => type switch
    {
        "gacha_ceil_item" => user.Data.userGachaCeilItems?.SingleOrDefault(i => i.gachaCeilItemId == id)?.quantity ?? 0,
        "gacha_ticket" => user.Data.userGachaTickets?.SingleOrDefault(i => i.gachaTicketId == id)?.quantity ?? 0,
        "material" => user.Data.userMaterials?.SingleOrDefault(i => i.materialId == id)?.quantity ?? 0,
        "practice_ticket" => user.Data.userPracticeTickets?.SingleOrDefault(i => i.practiceTicketId == id)?.quantity ?? 0,
        "coin" => user.Data.userGamedata?.coin ?? 0,
        "paid_jewel" => user.Data.userChargedCurrency?.paid ?? 0,
        "jewel" => (long)(user.Data.userChargedCurrency?.free ?? 0) + (user.Data.userChargedCurrency?.paid ?? 0),
        _ => throw new NotSupportedException("Unsupported gacha exchange cost.")
    };

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
            userExchange.exchangeRemaining -= exchangeCount;
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
        var card = cards.GetMasterCard(cardId) ?? throw new InvalidOperationException("Missing gacha card definition.");
        var isNew = user.Data.userCards?.All(card => card.cardId != cardId) ?? true;
        resources.Grant(new UserResource
        {
            resourceType = "card",
            resourceId = cardId,
            resourceLevel = 1,
            quantity = 1
        });
        if (isNew)
        {
            user.Data.userCards = user.Data.userCards!.OrderBy(c => c.cardId).ToArray();
            missions.RecordCardAcquisition(cardId, card.characterId);
        }
        else missions.RecordDuplicateCard(card.characterId);
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

}

public sealed class GachaRequestRejectedException(int status, string code = "") : Exception
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
