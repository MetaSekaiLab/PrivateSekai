extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Shop;

public sealed class VirtualShopService(UserSession user, ShopMasterQueries master,
    ResourceMasterQueries resourceMaster, ResourceService resources)
{
    public (int Status, string ErrorCode, UserResource[] Rewards) Purchase(int shopId, int itemId, string costType)
    {
        var shop = master.GetVirtualShop(shopId);
        var item = shop?.virtualShopItems?.SingleOrDefault(i => i.id == itemId);
        if (item == null) return (404, "", []);
        var offer = user.Data.userVirtualShops?.SingleOrDefault(s => s.virtualShopId == shopId)?.userVirtualShopItems?
            .SingleOrDefault(i => i.virtualShopItemId == itemId);
        if (offer == null) return (404, "", []);
        if (offer.status != "sale" || user.Now < item.startAt || (item.endAt.HasValue && user.Now >= item.endAt.Value))
            return (409, "", []);
        var cost = costType switch
        {
            "VIRTUAL_COIN" => item.costVirtualCoin,
            "JEWEL" => item.costJewel,
            "PAID_JEWEL" => item.costPaidJewel,
            _ => null
        };
        if (cost == null || cost < 0) return (409, "", []);
        var rewards = resourceMaster.BuildResourcesFromBox("virtual_shop_item", item.resourceBoxId);
        if (rewards.Any(IsOwned)) return (409, "", []);
        var balance = costType switch
        {
            "VIRTUAL_COIN" => user.Data.userGamedata?.virtualCoin ?? 0,
            "PAID_JEWEL" => Math.Max(0, user.Data.userChargedCurrency?.paid ?? 0),
            _ => (long)Math.Max(0, user.Data.userChargedCurrency?.free ?? 0) + Math.Max(0, user.Data.userChargedCurrency?.paid ?? 0)
        };
        if (balance < cost.Value) return (409, "not_enough_resource", []);
        if (item.virtualShopItemFirstPurchaseBonusId != 0 || item.bonusResourceBoxId != 0 ||
            rewards.Length == 0 || rewards.Any(r => r.quantity != 1 || r.resourceId <= 0 || r.resourceType is not
                ("avatar_motion" or "avatar_costume" or "avatar_accessory" or "avatar_coordinate" or "avatar_skin_color" or "penlight")))
            throw new NotSupportedException("Special virtual shop purchase is not verified.");
        resources.Consume(costType.ToLowerInvariant(), 0, cost.Value);
        resources.Grant(rewards);
        offer.status = "sold_out";
        foreach (var reward in rewards)
        {
            var boxes = resourceMaster.FindSingleResourceBoxIds("virtual_shop_item", reward.resourceType, reward.resourceId).ToHashSet();
            var matching = master.GetVirtualShops().SelectMany(s => (s.virtualShopItems ?? []).Where(i => i.virtualShopItemFirstPurchaseBonusId == 0 &&
                    i.bonusResourceBoxId == 0 && boxes.Contains(i.resourceBoxId)).Select(i => (Shop: s.id, Item: i.id))).ToHashSet();
            foreach (var saved in user.Data.userVirtualShops ?? [])
                foreach (var row in saved.userVirtualShopItems ?? [])
                    if (matching.Contains((saved.virtualShopId, row.virtualShopItemId))) row.status = "sold_out";
        }
        user.MarkChanged(nameof(SuiteUser.userVirtualShops));
        return (200, "", rewards);
    }

    private bool IsOwned(UserResource resource) => resource.resourceType switch
    {
        "avatar_motion" => user.Data.userAvatarMotions?.Any(x => x.avatarMotionId == resource.resourceId) == true,
        "avatar_costume" => user.Data.userAvatarCostumes?.Any(x => x.avatarCostumeId == resource.resourceId) == true,
        "avatar_accessory" => user.Data.userAvatarAccessories?.Any(x => x.avatarAccessoryId == resource.resourceId) == true,
        "avatar_coordinate" => user.Data.userAvatarCoordinates?.Any(x => x.avatarCoordinateId == resource.resourceId) == true,
        "avatar_skin_color" => user.Data.userAvatarSkinColors?.Any(x => x.avatarSkinColorId == resource.resourceId) == true,
        "penlight" => user.Data.userPenlights?.Any(x => x.penlightId == resource.resourceId) == true,
        _ => false
    };
}
