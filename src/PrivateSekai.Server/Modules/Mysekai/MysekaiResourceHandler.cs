extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Mysekai;

public sealed class MysekaiResourceHandler(ResourceMasterQueries master) : IResourceHandler
{
    public IReadOnlyCollection<string> ResourceTypes { get; } = ["mysekai_item", "mysekai_tool"];

    public void Grant(UserSession user, UserResource resource)
    {
        if (resource.resourceId <= 0)
            return;

        switch (resource.resourceType)
        {
            case "mysekai_item":
                var items = (user.Data.userMysekaiItems ?? []).ToList();
                var item = items.FirstOrDefault(i => i.mysekaiItemId == resource.resourceId);
                if (item == null)
                {
                    item = new UserMysekaiItem { mysekaiItemId = resource.resourceId };
                    items.Add(item);
                }
                item.quantity += resource.quantity;
                item.lastObtainedAt = user.Now;
                user.Data.userMysekaiItems = items.OrderBy(i => i.mysekaiItemId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userMysekaiItems));
                break;
            case "mysekai_tool":
                var tools = (user.Data.userMysekaiTools ?? []).ToList();
                var tool = tools.FirstOrDefault(t => t.mysekaiToolId == resource.resourceId);
                if (tool == null)
                {
                    tool = new UserMysekaiTool
                    {
                        mysekaiToolId = resource.resourceId,
                        durability = master.GetMysekaiToolMaxDurability(resource.resourceId)
                    };
                    tools.Add(tool);
                }
                tool.quantity += resource.quantity;
                tool.lastObtainedAt = user.Now;
                user.Data.userMysekaiTools = tools.OrderBy(t => t.mysekaiToolId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userMysekaiTools));
                break;
            default:
                throw new NotSupportedException($"Resource '{resource.resourceType}' cannot be granted.");
        }
    }
}
