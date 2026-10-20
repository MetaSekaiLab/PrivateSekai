extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Modules.Profiles;
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
    CostumeService costumes,
    Random? random = null) : IUserRefreshHandler
{
    public UserGachaResponse ExecuteGacha(int gachaId, int gachaBehaviorId, bool isPriorityUsePaidJewel, int? executeCount = null)
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
        // 指定抽数由客户端选择同消耗类型的单抽行为，用于限量池收尾。
        if (executeCount is <= 0 || (executeCount.HasValue && spinCount != 1))
            throw new GachaRequestRejectedException(400);
        var executions = executeCount ?? 1;
        if (executeCount.HasValue) spinCount = executions;
        var behaviorType = behavior.gachaBehaviorType;
        PrepareRefresh();
        ValidateGachaLimits(gacha, behavior, spinCount, executions);

        var paidBefore = user.Data.userChargedCurrency?.paid ?? 0;
        var consumedCosts = ConsumeGachaCost(behavior, isPriorityUsePaidJewel, executions);

        var bonusCardIds = ApplyGachaBonus(gacha, behavior, spinCount, paidBefore - (user.Data.userChargedCurrency?.paid ?? 0), executions);
        if (bonusCardIds.Length > spinCount) throw new InvalidOperationException("Gacha bonus exceeds the requested spin count.");
        var drawnCardIds = DrawGachaCards(gacha, behaviorType, spinCount - bonusCardIds.Length);
        var obtainPrizes = new List<UserGachaSpinObtainPrize>();
        foreach (var (cardId, lotteryType) in bonusCardIds.Select(id => (id, "fixed_gacha_bonus"))
            .Concat(drawnCardIds.Select(id => (id, "normal"))))
        {
            var isNew = GrantGachaCard(cardId);
            var costume3ds = isNew ? costumes.ObtainForCard(cardId) : [];
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
                gachaLotteryType = lotteryType,
                costume3d = costume3ds,
                cardExtra = []
            });
        }

        var userGacha = UpsertUserGacha(gachaId, gachaBehaviorId, now, executions);
        if (behavior.resourceCategory == "free_resource" && behavior.gachaBehaviorType is "once_a_day" or "once_a_week")
        {
            var free = (user.Data.userGachaFreeResources ?? []).ToList();
            var saved = free.SingleOrDefault(f => f.gachaBehaviorType == behavior.gachaBehaviorType);
            if (saved == null)
            {
                saved = new UserGachaFreeResource { userId = user.UserId, gachaBehaviorType = behavior.gachaBehaviorType };
                free.Add(saved);
            }
            saved.lastSpinAt = checked((ulong)now);
            user.Data.userGachaFreeResources = free.OrderBy(f => f.gachaBehaviorType, StringComparer.Ordinal).ToArray();
            user.MarkChanged(nameof(SuiteUser.userGachaFreeResources));
        }
        var obtainGachaCeilItems = behavior.resourceCategory == "free_resource" && behavior.gachaSpinnableType == "colorful_pass"
            ? [] : GrantGachaCeilItem(gacha, gachaId, spinCount);
        GrantExtra(behavior.gachaExtraId, executions);
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

    private void GrantExtra(int extraId, int executions)
    {
        if (extraId == 0) return;
        var box = master.GetExtraResourceBox(extraId);
        foreach (var resource in resourceMaster.BuildResourcesFromBox("gacha_extra", box.id))
        {
            resource.quantity = checked(resource.quantity * executions);
            if (resource.resourceType == "costume_3d") costumes.Obtain(resource.resourceId);
            else resources.Grant(resource);
        }
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

    public void PrepareRefresh()
    {
        foreach (var saved in user.Data.userGachas ?? [])
        {
            var gacha = master.GetMasterGacha(saved.gachaId);
            var behavior = master.GetMasterGachaBehavior(gacha, saved.gachaBehaviorId);
            if (gacha == null || !WithinPeriod(gacha.startAt, gacha.endAt) ||
                behavior?.gachaBehaviorType != "once_a_day" || behavior.resourceCategory == "free_resource" ||
                behavior.executeLimit <= 0 || saved.count == 0) continue;
            if (saved.lastSpinAt < DayStart(user.Now))
            {
                saved.count = 0;
                user.MarkChanged(nameof(SuiteUser.userGachas));
            }
        }
    }

    private long DayStart(long timestamp)
    {
        // 客户端 SaveSystemData 使用 UTC+9，日切小时来自 master。
        var offset = TimeSpan.FromHours(9);
        var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToOffset(offset);
        var start = date.Date.AddHours(master.GetDayChangeHour());
        if (date.DateTime < start) start = start.AddDays(-1);
        return new DateTimeOffset(start, offset).ToUnixTimeMilliseconds();
    }

    private void ValidateGachaLimits(MasterGacha gacha, MasterGachaBehavior behavior, int spinCount, int executions)
    {
        var count = user.Data.userGachas?.SingleOrDefault(g => g.gachaId == gacha.id && g.gachaBehaviorId == behavior.id)?.count ?? 0;
        if (count < 0 || behavior.executeLimit < 0 || gacha.spinLimit < 0)
            throw new InvalidOperationException("Invalid gacha count or limit.");
        var pass = user.Data.userColorfulPassV2;
        var activePass = pass != null && pass.expiredAt >= checked((ulong)user.Now);
        var limit = behavior.executeLimit;
        if (limit > 0 && behavior.gachaBehaviorType == "once_a_day" && behavior.costResourceType == "paid_jewel" && activePass)
            limit = master.GetColorfulPass(pass!.colorfulPassId).dailyPaidGachaSpinLimit;
        if (behavior.resourceCategory == "free_resource" && behavior.gachaBehaviorType is "once_a_day" or "once_a_week")
        {
            if (!activePass) throw new GachaRequestRejectedException(409);
            var definition = master.GetColorfulPass(pass!.colorfulPassId);
            var allowed = behavior.gachaBehaviorType == "once_a_day" ? definition.dailyFreeGachaSpinLimit : definition.weeklyColorfulPassGachaSpinLimit;
            if (allowed <= 0 || spinCount > allowed) throw new GachaRequestRejectedException(409);
            var last = user.Data.userGachaFreeResources?.SingleOrDefault(f => f.gachaBehaviorType == behavior.gachaBehaviorType)?.lastSpinAt;
            if (last.HasValue)
            {
                if (behavior.gachaBehaviorType == "once_a_week")
                {
                    var offset = TimeSpan.FromHours(9);
                    var date = DateTimeOffset.FromUnixTimeMilliseconds(checked((long)last.Value)).ToOffset(offset);
                    var hour = master.GetDayChangeHour();
                    var days = (8 - (int)date.DayOfWeek) % 7;
                    if (days == 0 && date.Hour >= hour) days = 7;
                    var next = new DateTimeOffset(date.Date.AddDays(days).AddHours(hour), offset).ToUnixTimeMilliseconds();
                    if (user.Now / 1000 * 1000 <= next) throw new GachaRequestRejectedException(409);
                }
                else if (checked((long)last.Value) >= DayStart(user.Now)) throw new GachaRequestRejectedException(409);
            }
        }
        else if (limit > 0 && (long)count + executions > limit) throw new GachaRequestRejectedException(409);
        if (gacha.spinLimit > 0)
        {
            var behaviors = gacha.gachaBehaviors.ToDictionary(b => b.id);
            var used = (user.Data.userGachas ?? []).Where(g => g.gachaId == gacha.id)
                .Sum(g => checked((long)behaviors[g.gachaBehaviorId].spinCount * g.count));
            if (spinCount > gacha.spinLimit - used) throw new GachaRequestRejectedException(409);
        }
    }

    private int[] ApplyGachaBonus(MasterGacha gacha, MasterGachaBehavior behavior, int spinCount, int paidConsumed, int executions)
    {
        if (gacha.gachaBonusId <= 0 || behavior.resourceCategory == "free_resource") return [];
        var bonus = master.GetGachaBonus(gacha.gachaBonusId);
        var defaultRate = master.GetGachaBonusPointRate(false);
        var paidSpins = behavior.costResourceType == "paid_jewel" ? spinCount : 0;
        if (behavior.costResourceType == "jewel")
        {
            var quantity = checked((behavior.costResourceQuantity ?? 0) * executions);
            if (quantity <= 0 || quantity % spinCount != 0)
                throw new InvalidOperationException("Invalid per-spin gacha jewel cost.");
            paidSpins = Math.Min(spinCount, paidConsumed / (quantity / spinCount));
        }
        var increment = defaultRate * (spinCount - paidSpins) + master.GetGachaBonusPointRate(true) * paidSpins;
        var points = (user.Data.userGachaBonusPoints ?? []).ToList();
        var point = points.SingleOrDefault(p => p.gachaId == gacha.id);
        if (point == null)
        {
            point = new UserGachaBonusPoint { userId = user.UserId, gachaId = gacha.id };
            points.Add(point);
        }
        if (!float.IsFinite(point.gachaBonusPoint) || point.gachaBonusPoint < 0 || point.gachaBonusPoint >= 100 ||
            !float.IsFinite(point.totalGachaBonusPoint) || point.totalGachaBonusPoint < 0)
            throw new InvalidOperationException("Invalid saved gacha bonus points.");
        var after = point.gachaBonusPoint + increment;
        var obtained = new List<int>();
        for (var border = (int)(point.gachaBonusPoint / 50) + 1; border <= (int)(after / 50); border++)
        {
            if (border % 2 != 0)
                obtained.Add(DrawGachaCardByRarity(gacha, "rarity_4", null, bonus.switchFixedBonusWeight));
            else
            {
                var pickups = (gacha.gachaPickups ?? []).Where(p => p.gachaPickupType == "normal").Select(p => p.cardId).Distinct().ToArray();
                if (pickups.Length == 0 || pickups.Any(id => cards.GetCardRarityType(id) != "rarity_4" || !gacha.gachaDetails.Any(d => d.cardId == id)))
                    throw new InvalidOperationException("Missing gacha bonus pickup pool.");
                obtained.Add(pickups[(int)(random ?? Random.Shared).NextInt64(pickups.Length)]);
            }
        }
        point.gachaBonusPoint = after % 100;
        point.totalGachaBonusPoint += increment;
        user.Data.userGachaBonusPoints = points.OrderBy(p => p.gachaId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userGachaBonusPoints));
        return obtained.ToArray();
    }

    private static int GetGachaSpinCount(MasterGachaBehavior? behavior)
    {
        var spinCount = behavior?.spinCount ?? 0;
        if (spinCount > 0)
            return spinCount;

        throw new InvalidOperationException("Missing gacha spin count.");
    }

    private UserResource[] ConsumeGachaCost(MasterGachaBehavior? behavior, bool isPriorityUsePaidJewel, int executions)
    {
        if (string.Equals(behavior?.resourceCategory, "free_resource", StringComparison.Ordinal))
            return [];

        var costResourceType = behavior?.costResourceType;
        var costResourceId = behavior?.costResourceId ?? 0;
        var costQuantity = behavior?.costResourceQuantity ?? 0;
        if (costQuantity <= 0 || string.IsNullOrEmpty(costResourceType))
            throw new InvalidOperationException("Missing gacha cost.");
        if (executions > int.MaxValue / costQuantity) throw new GachaRequestRejectedException(400);
        costQuantity *= executions;

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

    private int DrawGachaCardByRarity(MasterGacha? gacha, string? rarityType, Func<int, bool>? cardFilter, bool useFixedBonusWeight = false)
    {
        if (gacha?.gachaDetails == null)
            throw new InvalidOperationException("Missing gacha card pool.");

        var candidates = gacha.gachaDetails
            .Select(detail => new
            {
                CardId = detail.cardId,
                Weight = useFixedBonusWeight ? detail.fixedBonusWeight : detail.weight
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

    private UserGacha UpsertUserGacha(int gachaId, int gachaBehaviorId, long now, int executions)
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

        userGacha.count = checked(userGacha.count + executions);
        userGacha.lastSpinAt = now;
        user.Data.userGachas = gachas.OrderBy(g => g.gachaId).ThenBy(g => g.gachaBehaviorId).ToArray();
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
