extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Shop;

public sealed class AreaItemResourceHandler(ShopMasterQueries master) : IResourceHandler
{
    public IReadOnlyCollection<string> ResourceTypes { get; } = ["area_item"];

    public void Grant(UserSession user, UserResource resource)
    {
        var definition = master.GetAreaItem(resource.resourceId)
            ?? throw new ArgumentException("Unknown area item.");
        var area = user.Data.userAreas?.SingleOrDefault(a => a.areaId == definition.areaId)
            ?? throw new ArgumentException("Area is unavailable.");
        var items = (area.areaItems ?? []).ToList();
        var item = items.SingleOrDefault(i => i.areaItemId == resource.resourceId);
        if (resource.quantity != 1 || resource.resourceLevel != (item?.level ?? 0) + 1)
            throw new ArgumentException("Invalid area item upgrade.");
        if (item == null)
        {
            item = new UserAreaItem { areaItemId = resource.resourceId };
            items.Add(item);
        }
        item.level = resource.resourceLevel;
        area.areaItems = items.ToArray();
        user.MarkChanged(nameof(SuiteUser.userAreas));
    }
}
