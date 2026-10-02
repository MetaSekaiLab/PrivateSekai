extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Shop;

public sealed class ShopService(
    UserSession user,
    ShopMasterQueries master,
    ResourceMasterQueries resourceMaster,
    ResourceService resourceService)
{
    public void PurchaseShopItem(int shopId, int shopItemId)
    {
        var shopItem = master.GetMasterShopItem(shopId, shopItemId);
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
