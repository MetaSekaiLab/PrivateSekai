extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Shop;

public sealed class ShopMasterQueries(MasterData master)
{
    public MasterVirtualShop? GetVirtualShop(int id) =>
        master.GetTable<MasterVirtualShop>("virtualShops", s => s.id).FindById(id);

    public IReadOnlyList<MasterVirtualShop> GetVirtualShops() =>
        master.GetTable<MasterVirtualShop>("virtualShops", s => s.id).Rows;

    public MasterStamp? GetStamp(int id) =>
        master.GetTable<MasterStamp>("stamps", s => s.id).FindById(id);

    public MasterEventExchangeSummary? GetEventExchangeSummary(int exchangeId) =>
        master.GetTable<MasterEventExchangeSummary>("eventExchangeSummaries").Rows
            .SingleOrDefault(s => s.eventExchanges?.Any(e => e.id == exchangeId) == true);

    public MasterMaterialExchange? GetMaterialExchange(int id) =>
        master.GetTable<MasterMaterialExchange>("materialExchanges", e => e.id).FindById(id);

    public MasterMaterialExchangeSummary? GetMaterialExchangeSummary(int id) =>
        master.GetTable<MasterMaterialExchangeSummary>("materialExchangeSummaries", s => s.Id).FindById(id);

    public MasterResourceBox? GetMaterialExchangeFreebieBox(MasterMaterialExchangeSummary summary, int cardId)
    {
        var group = summary.materialExchangeFreebieGroupJson;
        if (group == null) return null;
        if (group.id <= 0 || group.materialExchangeFreebieType != "exchange_already_owned_card")
            throw new InvalidOperationException("Invalid material exchange freebie group.");
        var card = master.GetTable<MasterCard>("cards", c => c.id).FindById(cardId)
            ?? throw new InvalidOperationException("Missing exchanged card definition.");
        var freebie = summary.materialExchangeFreebies?.SingleOrDefault(f => f.materialExchangeFreebieGroupId == group.id
            && f.materialExchangeFreebieTypeId == card.characterId)
            ?? throw new InvalidOperationException("Missing material exchange freebie mapping.");
        var box = master.GetTable<MasterResourceBox>("resourceBoxes", b => b.id).Rows.SingleOrDefault(b =>
            b.resourceBoxPurpose == "material_exchange_freebie" && b.id == freebie.resourceBoxId)
            ?? throw new InvalidOperationException("Missing material exchange freebie box.");
        if (box.resourceBoxType is not ("expand" or "list") || box.details == null || box.details.Length == 0
            || box.details.Any(d => d.resourceType is not ("material" or "practice_ticket" or "skill_practice_ticket")
                || d.resourceId <= 0 || d.resourceQuantity <= 0))
            throw new InvalidOperationException("Invalid material exchange freebie resources.");
        return box;
    }

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
