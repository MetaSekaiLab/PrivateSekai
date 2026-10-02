extern alias game;

using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Shop;

public sealed class ShopMasterQueries(MasterData master)
{
    public MasterAreaItem? GetAreaItem(int id) =>
        master.GetTable<MasterAreaItem>("areaItems", i => i.id).FindById(id);

    public MasterShopItem? GetNextAreaItemOffer(int shopId, int areaItemId, int level)
    {
        var boxes = master.GetTable<MasterResourceBox>("resourceBoxes").Rows
            .Where(b => b.resourceBoxPurpose == "shop_item" && b.details?.Length == 1 &&
                b.details[0].resourceType == "area_item" && b.details[0].resourceId == areaItemId &&
                b.details[0].resourceLevel == level + 1).Select(b => b.id).ToHashSet();
        return master.GetTable<MasterShopItem>("shopItems").Rows
            .SingleOrDefault(i => i.shopId == shopId && boxes.Contains(i.resourceBoxId));
    }

    public MasterShopItem? GetMasterShopItem(int shopId, int shopItemId) =>
        master.GetTable<MasterShopItem>("shopItems", i => i.id).Rows
            .FirstOrDefault(i => i.id == shopItemId && i.shopId == shopId);
}
