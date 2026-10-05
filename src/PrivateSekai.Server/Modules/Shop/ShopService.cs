extern alias game;

using System;
using System.Linq;
using game::Sekai;
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
    ResourceService resourceService)
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
        if (definition.refreshCycle != "none" || definition.exchangeLimit != 0 ||
            definition.materialExchangeRelationParents?.Count > 0 || summary.materialExchangeType != "normal" ||
            summary.materialExchangeFreebieGroupJson != null || summary.materialExchangeFreebies?.Length > 0)
            return 501;
        if (user.Now < definition.startAt || (definition.endAt > 0 && user.Now >= definition.endAt) ||
            user.Now < summary.StartAt || (summary.EndAt > 0 && user.Now >= summary.EndAt))
            throw new ArgumentException("Material exchange is not available.");
        var costs = (definition.costs ?? []).Where(c => c.costGroupId == costGroupId).ToArray();
        if (costs.Length == 0) throw new ArgumentException("Unknown material exchange cost group.");
        if (costs.Any(c => c.resourceType != "material" || c.resourceId <= 0 || c.quantity <= 0)) return 501;
        var amounts = costs.GroupBy(c => c.resourceId).ToDictionary(g => g.Key, g => checked(g.Sum(c => c.quantity) * count));
        if (amounts.Any(c => ((user.Data.userMaterials ?? []).SingleOrDefault(m => m.materialId == c.Key)?.quantity ?? 0) < c.Value))
            return 409;
        var rewards = resourceMaster.BuildResourcesFromBox("material_exchange", definition.resourceBoxId);
        if (rewards.Length == 0) throw new InvalidOperationException("Missing material exchange rewards.");
        // 先接入已核验的练习券兑换，不推定其他资源的附加联动。
        if (rewards.Any(r => r.resourceType != "practice_ticket" || r.quantity <= 0)) return 501;
        foreach (var reward in rewards) reward.quantity = checked(reward.quantity * count);
        var exchanges = (user.Data.userMaterialExchanges ?? []).ToList();
        var record = exchanges.SingleOrDefault(e => e.materialExchangeId == id);
        var nextCount = checked((record?.exchangeCount ?? 0) + count);
        var nextTotal = checked((record?.totalExchangeCount ?? 0) + count);
        foreach (var cost in amounts) resourceService.Consume("material", cost.Key, cost.Value);
        resourceService.Grant(rewards);
        if (record == null)
        {
            record = new UserMaterialExchange { userId = user.UserId, materialExchangeId = id };
            exchanges.Add(record);
        }
        record.exchangeCount = nextCount;
        record.totalExchangeCount = nextTotal;
        record.lastExchangedAt = user.Now;
        record.exchangeStatus = "exchangeable";
        user.Data.userMaterialExchanges = exchanges.OrderBy(e => e.materialExchangeId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userMaterialExchanges));
        return 200;
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
                return (200, missions.RecordStampPurchase(stamp.characterId1), true, "");
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
