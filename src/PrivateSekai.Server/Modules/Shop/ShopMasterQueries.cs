extern alias game;

using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Shop;

public sealed class ShopMasterQueries(MasterData master)
{
    public MasterShopItem? GetMasterShopItem(int shopId, int shopItemId) =>
        master.GetTable<MasterShopItem>("shopItems", i => i.id).Rows
            .FirstOrDefault(i => i.id == shopItemId && i.shopId == shopId);
}
