extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Profiles;

public sealed class CostumeService(UserSession user, CostumeMasterQueries master, MissionService missions, ResourceService resources)
{
    public (UserResource[] Costs, UserResource[] Rewards, UserCharacterMissionV2Status[] Achieved) Craft(int shopItemId)
    {
        var definition = master.GetShopItem(shopItemId) ?? throw new ArgumentException("Unknown costume shop item.");
        var offer = user.Data.userCostume3dShopItems?.SingleOrDefault(s => s.costume3dShopItemId == shopItemId);
        if (offer?.status != "sale" || user.Now < definition.startAt ||
            (definition.endAt.HasValue && user.Now >= definition.endAt.Value))
            throw new ArgumentException("Costume shop item is unavailable.");
        if (definition.headCostume3dId != 0 || definition.bodyCostume3dId <= 0)
            throw new NotSupportedException("Costume bundles with accessories are not verified.");
        var costume = master.GetCostume(definition.bodyCostume3dId) ?? throw new InvalidOperationException("Missing costume definition.");
        if (costume.partType != "body" || costume.costume3dType != "normal")
            throw new NotSupportedException("Costume crafting type is not verified.");
        if (user.Data.userCostume3dStatuses?.Any(c => c.costume3dId == costume.id && c.status == "sale") != true)
            throw new ArgumentException("Costume is not on sale.");
        if (definition.costs == null || definition.costs.Length == 0)
            throw new InvalidOperationException("Missing costume costs.");
        var costs = definition.costs.Select(c => new UserResource
        {
            resourceType = c.resourceType, resourceId = c.resourceId, quantity = c.resourceQuantity
        }).ToArray();
        if (costs.Any(c => c.resourceType != "material" || c.quantity <= 0))
            throw new NotSupportedException("Costume crafting costs are not verified.");
        foreach (var group in costs.GroupBy(c => c.resourceId))
            if ((user.Data.userMaterials?.SingleOrDefault(m => m.materialId == group.Key)?.quantity ?? 0) < group.Sum(c => (long)c.quantity))
                throw new ArgumentException("Insufficient costume materials.");
        foreach (var cost in costs) resources.Consume(cost.resourceType, cost.resourceId, cost.quantity);
        UserResource[] rewards = [new() { resourceType = "costume_3d", resourceId = costume.id, quantity = 1 }];
        resources.Grant(rewards);
        offer.status = "sold_out";
        user.MarkChanged(nameof(SuiteUser.userCostume3dShopItems));
        return (costs, rewards, missions.RecordCostumeCraft(costume.characterId));
    }

    public void Save(int characterId, string unit, UserCharacterCostume3DRequest request)
    {
        var current = user.Data.userCharacterCostume3ds?.SingleOrDefault(c => c.characterId == characterId &&
            c.unit.ToUpperInvariant() == unit) ?? throw new ArgumentException("Unknown character costume slot.");
        foreach (var (id, part) in new[] { (request.headCostume3dId, "head"), (request.bodyCostume3dId, "body"), (request.hairCostume3dId, "hair") })
        {
            var costume = master.GetCostume(id) ?? throw new ArgumentException("Unknown costume.");
            if (costume.characterId != characterId || costume.partType != part)
                throw new ArgumentException("Costume does not match character or part.");
            if (user.Data.userCostume3dStatuses?.Any(c => c.costume3dId == id && c.status == "available") != true)
                throw new ArgumentException("Costume is not available.");
        }
        current.headCostume3dId = request.headCostume3dId;
        current.bodyCostume3dId = request.bodyCostume3dId;
        current.hairCostume3dId = request.hairCostume3dId;
        user.MarkChanged(nameof(SuiteUser.userCharacterCostume3ds));
        missions.RecordCostumeChange();
    }
}
