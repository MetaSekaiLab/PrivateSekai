extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Profiles;

public sealed class CostumeMasterQueries(MasterData master)
{
    public MasterCostume3D? GetCostume(int id) =>
        master.GetTable<MasterCostume3D>("costume3ds", c => c.id).FindById(id);

    public MasterCostume3DShopItem? GetShopItem(int id) =>
        master.GetTable<MasterCostume3DShopItem>("costume3dShopItems", c => c.id).FindById(id);

    public MasterCostume3D[] GetInitialCardCostumes(int cardId) =>
        master.GetTable<MasterCardCostume3D>("cardCostume3ds").Rows.Where(c => c.cardId == cardId)
            .Select(c => (Mapping: c, Costume: GetCostume(c.costume3dId) ?? throw new InvalidOperationException("Missing card costume definition.")))
            .Where(c => c.Costume.partType != "hair" || c.Mapping.isInitialObtainHair).Select(c => c.Costume).ToArray();

    public MasterReleaseCondition[] GetInitialCardConditions(int cardId) =>
        master.GetTable<MasterReleaseCondition>("releaseConditions").Rows
            .Where(c => c.releaseConditionType == "master_rank" && c.releaseConditionTypeId == cardId && c.releaseConditionTypeLevel == 0).ToArray();

    public MasterCostume3DShopItem[] GetInitialCardShopItems(int cardId)
    {
        var conditions = GetInitialCardConditions(cardId).Select(c => c.id).ToHashSet();
        return master.GetTable<MasterCostume3DShopItem>("costume3dShopItems", c => c.id).Rows
            .Where(c => conditions.Contains(c.releaseConditionId)).ToArray();
    }
}
