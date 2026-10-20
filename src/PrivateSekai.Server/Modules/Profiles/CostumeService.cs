extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Profiles;

public sealed class CostumeService(UserSession user, CostumeMasterQueries master, MissionService missions, ResourceService resources)
{
    public bool Obtain(int costumeId)
    {
        var definition = master.GetCostume(costumeId) ?? throw new InvalidOperationException("Missing costume definition.");
        if (user.Data.userCostume3dStatuses?.Any(c => c.costume3dId == costumeId && c.status == "available") == true) return false;
        resources.Grant(new UserResource { resourceId = costumeId, resourceType = "costume_3d", quantity = 1 });
        if (definition.partType == "body") missions.RecordCostumeAcquisition(definition.characterId);
        return true;
    }

    public UserResource[] ObtainForCard(int cardId)
    {
        var definitions = master.GetInitialCardCostumes(cardId);
        if (definitions.Length == 0) return [];
        var released = (user.Data.userReleaseConditions ?? []).ToList();
        foreach (var condition in master.GetInitialCardConditions(cardId))
        {
            if (released.Any(c => c.releaseConditionId == condition.id)) continue;
            released.Add(new UserReleaseCondition { userId = user.UserId, releaseConditionId = condition.id, createdAt = user.Now });
            user.MarkChanged(nameof(SuiteUser.userReleaseConditions));
        }
        user.Data.userReleaseConditions = released.OrderBy(c => c.releaseConditionId).ToArray();
        var obtained = new List<UserResource>();
        foreach (var definition in definitions)
        {
            if (!Obtain(definition.id)) continue;
            var resource = new UserResource { resourceId = definition.id, resourceType = "costume_3d", quantity = 1 };
            obtained.Add(resource);
        }
        var offers = (user.Data.userCostume3dShopItems ?? []).ToList();
        var statuses = (user.Data.userCostume3dStatuses ?? []).ToList();
        foreach (var definition in master.GetInitialCardShopItems(cardId))
        {
            var offer = offers.SingleOrDefault(c => c.costume3dShopItemId == definition.id);
            if (offer == null)
            {
                offer = new UserCostume3DShopItem { costume3dShopItemId = definition.id, status = "forbidden" };
                offers.Add(offer);
            }
            if (offer.status == "forbidden") offer.status = "sale";
            foreach (var id in new[] { definition.headCostume3dId, definition.bodyCostume3dId }.Where(id => id > 0))
            {
                var status = statuses.SingleOrDefault(c => c.costume3dId == id);
                if (status == null)
                {
                    status = new UserCostume3DStatus { costume3dId = id, status = "forbidden" };
                    statuses.Add(status);
                }
                if (status.status == "forbidden") status.status = "sale";
            }
        }
        user.Data.userCostume3dShopItems = offers.OrderBy(c => c.costume3dShopItemId).ToArray();
        user.Data.userCostume3dStatuses = statuses.OrderBy(c => c.costume3dId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userCostume3dShopItems));
        user.MarkChanged(nameof(SuiteUser.userCostume3dStatuses));
        return obtained.ToArray();
    }

    public int Fix(int characterId, string? unit)
    {
        if (unit == null) return 400;
        user.Data.userFixCostumes = (user.Data.userFixCostumes ?? []).Where(c => c.characterId != characterId)
            .Append(new UserFixCostume { userId = user.UserId, characterId = characterId, unit = unit })
            .OrderBy(c => c.characterId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userFixCostumes));
        return 200;
    }

    public int Unfix(int characterId)
    {
        var current = user.Data.userFixCostumes ?? [];
        if (!current.Any(c => c.characterId == characterId)) return 404;
        user.Data.userFixCostumes = current.Where(c => c.characterId != characterId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userFixCostumes));
        return 200;
    }

    public (UserResource[] Costs, UserResource[] Rewards, UserCharacterMissionV2Status[] Achieved) Craft(int shopItemId)
    {
        var definition = master.GetShopItem(shopItemId) ?? throw new ArgumentException("Unknown costume shop item.");
        var offer = user.Data.userCostume3dShopItems?.SingleOrDefault(s => s.costume3dShopItemId == shopItemId);
        if (offer?.status != "sale" || user.Now < definition.startAt ||
            (definition.endAt.HasValue && user.Now >= definition.endAt.Value))
            throw new ArgumentException("Costume shop item is unavailable.");
        if (definition.bodyCostume3dId <= 0)
            throw new NotSupportedException("Costume crafting without a body is not verified.");
        var costume = master.GetCostume(definition.bodyCostume3dId) ?? throw new InvalidOperationException("Missing costume definition.");
        if (costume.partType != "body" || costume.costume3dType != "normal")
            throw new NotSupportedException("Costume crafting type is not verified.");
        if (user.Data.userCostume3dStatuses?.Any(c => c.costume3dId == costume.id && c.status == "sale") != true)
            throw new ArgumentException("Costume is not on sale.");
        if (definition.headCostume3dId != 0)
        {
            var head = master.GetCostume(definition.headCostume3dId) ?? throw new InvalidOperationException("Missing head costume definition.");
            if (head.partType != "head" || head.costume3dType != "normal" || head.characterId != costume.characterId)
                throw new NotSupportedException("Costume accessory type is not verified.");
            if (user.Data.userCostume3dStatuses?.Any(c => c.costume3dId == head.id && c.status == "sale") != true)
                throw new ArgumentException("Head costume is not on sale.");
        }
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
        var rewards = new[] { definition.headCostume3dId, costume.id }.Where(id => id > 0)
            .Select(id => new UserResource { resourceType = "costume_3d", resourceId = id, quantity = 1 }).ToArray();
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
