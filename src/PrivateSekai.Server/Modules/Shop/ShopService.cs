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
    public UserCharacterMissionV2Status[] PurchaseShopItem(int shopId, int shopItemId)
    {
        var shopItem = master.GetMasterShopItem(shopId, shopItemId);
        if (shopItem != null)
        {
            var areaRewards = resourceMaster.BuildResourcesFromBox("shop_item", shopItem.resourceBoxId);
            if (areaRewards.Any(r => r.resourceType == "area_item"))
            {
                PurchaseAreaItem(shopId, shopItem, areaRewards);
                if (areaRewards[0].resourceLevel == 1)
                    missions.RecordAreaItemPurchase();
                return missions.RecordAreaItemUpgrade(areaRewards[0].resourceId);
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
        return [];
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
