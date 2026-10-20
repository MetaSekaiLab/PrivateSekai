extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Shop;

public sealed class ShopService(
    UserSession user,
    ShopMasterQueries master,
    MissionService missions,
    ResourceMasterQueries resourceMaster,
    ResourceService resourceService,
    CardAcquisitionService acquisitions) : IUserRefreshHandler
{
    public (int Status, UserResource[] Rewards) ExchangeEvent(int id, int count)
    {
        if (count <= 0) throw new ArgumentException("Invalid event exchange count.");
        var summary = master.GetEventExchangeSummary(id) ?? throw new ArgumentException("Unknown event exchange.");
        var definition = summary.eventExchanges.Single(e => e.id == id);
        var record = user.Data.userEventExchanges?.SingleOrDefault(e => e.eventId == summary.eventId && e.eventExchangeId == id);
        if (record == null || record.exchangeStatus != "exchangeable" || definition.exchangeLimit < 0 ||
            (definition.exchangeLimit > 0 && count > record.exchangeRemaining) ||
            user.Now < summary.startAt || (summary.endAt > 0 && user.Now >= summary.endAt))
            return (501, []);
        var cost = definition.eventExchangeCost ?? throw new InvalidOperationException("Missing event exchange cost.");
        if (cost.resourceType != "event_item" || cost.resourceId <= 0 || cost.resourceQuantity <= 0) return (501, []);
        var quantity = checked(cost.resourceQuantity * count);
        if ((user.Data.userEventItems?.SingleOrDefault(i => i.eventItemId == cost.resourceId)?.quantity ?? 0) < quantity)
            return (409, []);
        var rewards = resourceMaster.BuildResourcesFromBox("event_exchange", definition.resourceBoxId);
        if (rewards.Length == 0) throw new InvalidOperationException("Missing event exchange rewards.");
        if (rewards.Any(r => r.resourceType is not ("skill_practice_ticket" or "coin" or "boost_item") || r.quantity <= 0)) return (501, []);
        foreach (var reward in rewards) reward.quantity = checked(reward.quantity * count);
        if (!missions.RecordEventItemConsumption(summary.eventId, quantity)) return (501, []);
        resourceService.Consume(cost.resourceType, cost.resourceId, quantity);
        resourceService.Grant(rewards);
        if (definition.exchangeLimit > 0)
        {
            record.exchangeRemaining -= count;
            if (record.exchangeRemaining == 0) record.exchangeStatus = "not_exchangeable";
        }
        user.MarkChanged(nameof(SuiteUser.userEventExchanges));
        return (200, rewards);
    }

    public int ExchangeMaterial(int id, int costGroupId, int count)
    {
        if (count <= 0 || costGroupId < 0) throw new ArgumentException("Invalid material exchange parameters.");
        var definition = master.GetMaterialExchange(id) ?? throw new ArgumentException("Unknown material exchange.");
        var summary = master.GetMaterialExchangeSummary(definition.materialExchangeSummaryId)
            ?? throw new InvalidOperationException("Missing material exchange summary.");
        if (definition.refreshCycle is not ("none" or "monthly") || summary.materialExchangeType != "normal" ||
            summary.materialExchangeFreebieGroupJson is { materialExchangeFreebieType: not "exchange_already_owned_card" })
            return 501;
        if (summary.materialExchangeFreebieGroupJson == null && summary.materialExchangeFreebies?.Length > 0)
            throw new InvalidOperationException("Missing material exchange freebie group.");
        if (user.Now < definition.startAt || (definition.endAt > 0 && user.Now >= definition.endAt) ||
            user.Now < summary.StartAt || (summary.EndAt > 0 && user.Now >= summary.EndAt))
            throw new ArgumentException("Material exchange is not available.");
        var monthly = definition.refreshCycle == "monthly";
        if (definition.exchangeLimit < 0 || monthly && definition.exchangeLimit == 0)
            throw new InvalidOperationException("Invalid material exchange limit.");
        var exchanges = (user.Data.userMaterialExchanges ?? []).ToList();
        var record = exchanges.SingleOrDefault(e => e.materialExchangeId == id);
        if (record is { exchangeCount: < 0 } or { totalExchangeCount: < 0 } or { refreshedAt: < 0 })
            throw new InvalidOperationException("Invalid saved material exchange counts.");
        var reset = monthly && record is { refreshedAt: > 0 } && record.refreshedAt <= user.Now;
        var nextCount = checked((reset ? 0 : record?.exchangeCount ?? 0) + count);
        var nextTotal = checked((record?.totalExchangeCount ?? 0) + count);
        if (definition.exchangeLimit > 0 && nextCount > definition.exchangeLimit) return 409;
        var costs = (definition.costs ?? []).Where(c => c.costGroupId == costGroupId).ToArray();
        if (costs.Length == 0) throw new ArgumentException("Unknown material exchange cost group.");
        if (costs.Any(c => c.resourceType is not ("material" or "mysekai_material") || c.resourceId <= 0 || c.quantity <= 0)) return 501;
        var amounts = costs.GroupBy(c => (c.resourceType, c.resourceId)).ToDictionary(g => g.Key, g => checked(g.Sum(c => c.quantity) * count));
        if (amounts.Any(c => (c.Key.resourceType == "material"
            ? user.Data.userMaterials?.SingleOrDefault(m => m.materialId == c.Key.resourceId)?.quantity ?? 0
            : user.Data.userMysekaiMaterials?.SingleOrDefault(m => m.mysekaiMaterialId == c.Key.resourceId)?.quantity ?? 0) < c.Value))
            return 409;
        var rewards = resourceMaster.BuildResourcesFromBox("material_exchange", definition.resourceBoxId);
        if (rewards.Length == 0) throw new InvalidOperationException("Missing material exchange rewards.");
        if (rewards.Any(r => r.resourceType is not ("practice_ticket" or "material" or "virtual_coin" or "mysekai_material" or "card") || r.quantity <= 0)) return 501;
        if (summary.materialExchangeFreebieGroupJson != null && rewards.Any(r => r.resourceType != "card"))
            throw new InvalidOperationException("Card exchange freebies require card rewards.");
        if (rewards.Any(r => r.resourceType != "virtual_coin" && r.resourceId <= 0) ||
            rewards.Any(r => r.resourceType == "virtual_coin") && user.Data.userGamedata == null)
            throw new InvalidOperationException("Invalid material exchange reward inventory.");
        foreach (var reward in rewards) reward.quantity = checked(reward.quantity * count);
        foreach (var cost in amounts) resourceService.Consume(cost.Key.resourceType, cost.Key.resourceId, cost.Value);
        foreach (var reward in rewards)
        {
            if (reward.resourceType != "card")
            {
                resourceService.Grant(reward);
                continue;
            }
            var freebie = master.GetMaterialExchangeFreebieBox(summary, reward.resourceId);
            var acquired = acquisitions.Grant(reward.resourceId, reward.quantity);
            var repeated = reward.quantity - (acquired.IsNew ? 1 : 0);
            if (freebie == null || repeated == 0) continue;
            foreach (var resource in resourceMaster.BuildResourcesFromBox("material_exchange_freebie", freebie.id))
            {
                resource.quantity = checked(resource.quantity * repeated);
                resourceService.Grant(resource);
            }
        }
        if (record == null)
        {
            record = new UserMaterialExchange { userId = user.UserId, materialExchangeId = id };
            exchanges.Add(record);
        }
        record.exchangeCount = nextCount;
        record.totalExchangeCount = nextTotal;
        record.lastExchangedAt = user.Now;
        record.exchangeStatus = definition.exchangeLimit > 0 && nextCount == definition.exchangeLimit ? "not_exchangeable" : "exchangeable";
        if (definition.exchangeLimit > 0) record.exchangeRemaining = definition.exchangeLimit - nextCount;
        if (monthly && record.refreshedAt <= user.Now) record.refreshedAt = NextMonthlyRefresh();
        user.Data.userMaterialExchanges = exchanges.OrderBy(e => e.materialExchangeId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userMaterialExchanges));
        return 200;
    }

    public void PrepareRefresh()
    {
        foreach (var record in user.Data.userMaterialExchanges ?? [])
        {
            var definition = master.GetMaterialExchange(record.materialExchangeId);
            if (definition?.refreshCycle != "monthly" || record.refreshedAt > user.Now ||
                user.Now < definition.startAt || definition.endAt > 0 && user.Now >= definition.endAt) continue;
            if (definition.exchangeLimit <= 0 || record.exchangeCount < 0 || record.totalExchangeCount < 0 || record.refreshedAt < 0)
                throw new InvalidOperationException("Invalid monthly material exchange state.");
            if (record.refreshedAt > 0) record.exchangeCount = 0;
            record.exchangeRemaining = checked(definition.exchangeLimit - record.exchangeCount);
            if (record.exchangeRemaining < 0) throw new InvalidOperationException("Material exchange count exceeds its limit.");
            record.exchangeStatus = record.exchangeRemaining == 0 ? "not_exchangeable" : "exchangeable";
            record.refreshedAt = NextMonthlyRefresh();
            user.MarkChanged(nameof(SuiteUser.userMaterialExchanges));
        }
    }

    private long NextMonthlyRefresh()
    {
        var offset = TimeSpan.FromHours(9);
        var date = DateTimeOffset.FromUnixTimeMilliseconds(user.Now).ToOffset(offset);
        return new DateTimeOffset(date.Year, date.Month, 1, 0, 0, 0, offset).AddMonths(1).ToUnixTimeMilliseconds();
    }

    public (int Status, UserCharacterMissionV2Status[] Achieved, bool ExcludeShop, string ErrorCode) PurchaseShopItem(int shopId, int shopItemId)
    {
        var shopItem = master.GetMasterShopItem(shopId, shopItemId);
        if (shopItem != null)
        {
            var areaRewards = resourceMaster.BuildResourcesFromBox("shop_item", shopItem.resourceBoxId);
            if (areaRewards.Any(r => r.resourceType == "music_vocal"))
            {
                if (areaRewards.Length != 1 || areaRewards[0].quantity != 1)
                    throw new NotSupportedException("Vocal shop reward combination is not verified.");
                var vocal = resourceMaster.GetMasterMusicVocal(areaRewards[0].resourceId)
                    ?? throw new InvalidOperationException("Missing music vocal definition.");
                if (vocal.musicVocalType != "another_vocal" || vocal.characters?.Length is null or 0 ||
                    vocal.characters.Any(c => c.characterType != "game_character" || c.characterId <= 0))
                    throw new NotSupportedException("Vocal character mapping is not verified.");
                var offer = user.Data.userShops?.SingleOrDefault(s => s.shopId == shopId)?.userShopItems?
                    .SingleOrDefault(i => i.shopItemId == shopItemId);
                if (offer?.status != "sale" || user.Data.userMusicVocals?.Any(v => v.musicVocalId == vocal.id) == true)
                    return (409, [], false, "");
                if (shopItem.costs == null || shopItem.costs.Length == 0)
                    throw new InvalidOperationException("Missing vocal shop costs.");
                var costs = shopItem.costs.Select(c => c.cost ?? throw new InvalidOperationException("Missing vocal shop cost.")).ToArray();
                if (costs.Any(c => c.resourceType != "material" || c.resourceId <= 0 || c.quantity <= 0))
                    throw new NotSupportedException("Vocal shop costs are not verified.");
                foreach (var group in costs.GroupBy(c => c.resourceId))
                    if ((user.Data.userMaterials?.SingleOrDefault(m => m.materialId == group.Key)?.quantity ?? 0) < group.Sum(c => (long)c.quantity))
                        return (409, [], false, "not_enough_resource");
                foreach (var cost in costs) resourceService.Consume(cost.resourceType, cost.resourceId, cost.quantity);
                resourceService.Grant(areaRewards);
                MarkShopItemSoldOut(shopId, shopItemId);
                return (200, missions.RecordAnotherVocalPurchase(vocal.characters.Select(c => c.characterId).Distinct().ToArray()), false, "");
            }
            if (areaRewards.Any(r => r.resourceType == "stamp"))
            {
                if (areaRewards.Length != 1 || areaRewards[0].quantity != 1)
                    throw new NotSupportedException("Stamp shop reward combination is not verified.");
                var stamp = master.GetStamp(areaRewards[0].resourceId) ?? throw new InvalidOperationException("Missing stamp definition.");
                if (stamp.stampType != "illustration" || stamp.characterId1 <= 0 ||
                    new[] { stamp.characterId2, stamp.characterId3, stamp.characterId4, stamp.characterId5 }.Any(id => id > 0))
                    throw new NotSupportedException("Stamp character mapping is not verified.");
                var offer = user.Data.userShops?.SingleOrDefault(s => s.shopId == shopId)?.userShopItems?
                    .SingleOrDefault(i => i.shopItemId == shopItemId);
                if (offer?.status != "sale" || user.Data.userStamps?.Any(s => s.stampId == stamp.id) == true)
                    return (409, [], true, "");
                if (shopItem.costs == null || shopItem.costs.Length == 0)
                    throw new InvalidOperationException("Missing stamp shop costs.");
                var costs = shopItem.costs.Select(c => c.cost ?? throw new InvalidOperationException("Missing stamp shop cost.")).ToArray();
                if (costs.Any(c => c.resourceType != "material" || c.resourceId <= 0 || c.quantity <= 0))
                    throw new NotSupportedException("Stamp shop costs are not verified.");
                foreach (var group in costs.GroupBy(c => c.resourceId))
                    if ((user.Data.userMaterials?.SingleOrDefault(m => m.materialId == group.Key)?.quantity ?? 0) < group.Sum(c => (long)c.quantity))
                        return (409, [], true, "not_enough_resource");
                foreach (var cost in costs) resourceService.Consume(cost.resourceType, cost.resourceId, cost.quantity);
                resourceService.Grant(areaRewards);
                MarkShopItemSoldOut(shopId, shopItemId);
                return (200, missions.RecordStampAcquisition(stamp.characterId1), true, "");
            }
            if (areaRewards.Any(r => r.resourceType == "area_item"))
            {
                PurchaseAreaItem(shopId, shopItem, areaRewards);
                if (areaRewards[0].resourceLevel == 1)
                    missions.RecordAreaItemPurchase();
                return (200, missions.RecordAreaItemUpgrade(areaRewards[0].resourceId), false, "");
            }
            if (areaRewards.Any(r => r.resourceType == "music"))
            {
                if (areaRewards.Length != 1 || areaRewards[0].quantity != 1)
                    throw new NotSupportedException("Music shop reward combination is not verified.");
                var offer = user.Data.userShops?.SingleOrDefault(s => s.shopId == shopId)?.userShopItems?
                    .SingleOrDefault(i => i.shopItemId == shopItemId);
                if (offer?.status != "sale" || user.Data.userMusics?.Any(m => m.musicId == areaRewards[0].resourceId) == true)
                    throw new ArgumentException("Music shop item is unavailable.");
                if (shopItem.costs == null || shopItem.costs.Length == 0)
                    throw new InvalidOperationException("Missing music shop costs.");
                foreach (var entry in shopItem.costs)
                {
                    var cost = entry.cost ?? throw new InvalidOperationException("Missing music shop cost.");
                    resourceService.Consume(cost.resourceType, cost.resourceId, cost.quantity);
                }
                resourceService.Grant(areaRewards);
                MarkShopItemSoldOut(shopId, shopItemId);
                missions.RecordMusicPurchase();
                return (200, [], false, "");
            }
        }
        var wasSoldOut = IsShopItemSoldOut(shopId, shopItemId);

        MarkShopItemSoldOut(shopId, shopItemId);

        if (!wasSoldOut && shopItem?.costs != null)
        {
            foreach (var entry in shopItem.costs)
            {
                var cost = entry.cost;
                if (cost?.resourceType is "material" or "coin" or "jewel")
                    resourceService.Consume(cost.resourceType, cost.resourceId, cost.quantity);
            }
        }

        var resourceBoxId = shopItem?.resourceBoxId ?? shopItemId;
        var rewards = resourceMaster.BuildResourcesFromBox("shop_item", resourceBoxId);
        resourceService.Grant(rewards.Where(r => r.resourceType is
            "music" or "music_vocal" or "jewel" or "coin" or "virtual_coin" or
            "material" or "practice_ticket" or "costume_3d"));
        return (200, [], false, "");
    }

    private void PurchaseAreaItem(int shopId, MasterShopItem definition, UserResource[] rewards)
    {
        if (rewards.Length != 1 || rewards[0].resourceType != "area_item")
            throw new InvalidOperationException("Unsupported area item reward box.");
        var offer = user.Data.userShops?.SingleOrDefault(s => s.shopId == shopId)?.userShopItems?
            .SingleOrDefault(i => i.shopItemId == definition.id)
            ?? throw new ArgumentException("Area item offer is unavailable.");
        var reward = rewards[0];
        if (offer.status != "sale" || offer.level != reward.resourceLevel)
            throw new ArgumentException("Area item offer is not on sale.");
        var next = master.GetNextAreaItemOffer(shopId, reward.resourceId, reward.resourceLevel)
            ?? throw new NotSupportedException("Final area item offer behavior is not verified.");
        if (definition.costs == null || definition.costs.Length == 0)
            throw new InvalidOperationException("Missing area item costs.");
        foreach (var entry in definition.costs)
        {
            var cost = entry.cost ?? throw new InvalidOperationException("Missing area item cost.");
            resourceService.Consume(cost.resourceType, cost.resourceId, cost.quantity);
        }
        resourceService.Grant(reward);
        offer.shopItemId = next.id;
        offer.level = reward.resourceLevel + 1;
        user.MarkChanged(nameof(SuiteUser.userShops));
    }

    private bool IsShopItemSoldOut(int shopId, int shopItemId) =>
        user.Data.userShops?
            .FirstOrDefault(s => s.shopId == shopId)?
            .userShopItems?
            .Any(i => i.shopItemId == shopItemId &&
                      string.Equals(i.status, UserShopItem.STATUS_SOLD_OUT, StringComparison.Ordinal)) == true;

    private void MarkShopItemSoldOut(int shopId, int shopItemId)
    {
        user.Data.userShops ??= [];
        var shops = user.Data.userShops.ToList();
        var shop = shops.FirstOrDefault(s => s.shopId == shopId);
        if (shop == null)
        {
            shop = new UserShop
            {
                shopId = shopId,
                userShopItems = []
            };
            shops.Add(shop);
            user.Data.userShops = shops.ToArray();
        }

        var items = (shop.userShopItems ?? []).ToList();
        var item = items.FirstOrDefault(i => i.shopItemId == shopItemId);
        if (item == null)
        {
            item = new UserShopItem
            {
                shopItemId = shopItemId
            };
            items.Add(item);
        }

        item.status = UserShopItem.STATUS_SOLD_OUT;
        shop.userShopItems = items.ToArray();
        user.MarkChanged(nameof(SuiteUser.userShops));
    }
}
