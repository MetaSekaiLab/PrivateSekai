extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Profiles;

public sealed class ProfileResourceHandler : IResourceHandler
{
    public IReadOnlyCollection<string> ResourceTypes { get; } = ["costume_3d", "avatar_motion"];

    public void Grant(UserSession user, UserResource resource)
    {
        if (resource.resourceId <= 0)
            return;

        switch (resource.resourceType)
        {
            case "costume_3d":
                var costumes = (user.Data.userCostume3dStatuses ?? []).ToList();
                var costume = costumes.SingleOrDefault(c => c.costume3dId == resource.resourceId);
                if (costume?.status == "available") return;
                if (costume == null)
                {
                    costume = new UserCostume3DStatus { costume3dId = resource.resourceId };
                    costumes.Add(costume);
                }
                costume.obtainedAt = user.Now;
                costume.status = "available";
                user.Data.userCostume3dStatuses = costumes.OrderBy(c => c.costume3dId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userCostume3dStatuses));
                user.MarkChanged(nameof(SuiteUser.userCostume3dShopItems));
                break;
            case "avatar_motion":
                var motions = (user.Data.userAvatarMotions ?? []).ToList();
                if (motions.Any(m => m.avatarMotionId == resource.resourceId))
                    return;
                motions.Add(new UserAvatarMotion { avatarMotionId = resource.resourceId });
                user.Data.userAvatarMotions = motions.OrderBy(m => m.avatarMotionId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userAvatarMotions));
                break;
            default:
                throw new NotSupportedException($"Resource '{resource.resourceType}' cannot be granted.");
        }
    }
}
