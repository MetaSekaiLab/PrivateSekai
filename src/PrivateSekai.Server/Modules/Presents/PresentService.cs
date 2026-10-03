extern alias game;

using System;
using System.Collections.Generic;
using game::Sekai;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Presents;

public sealed class PresentService(UserSession user, ResourceService resourceService)
{
    public void AddLoginRewards(IEnumerable<UserResource> resources, string reason)
    {
        user.Data.userPresents ??= [];
        foreach (var resource in resources)
            user.Data.userPresents.Add(new UserPresentData
            {
                presentId = Guid.NewGuid().ToString(), seq = long.MaxValue - user.Now,
                resourceType = resource.resourceType, resourceId = resource.resourceId,
                resourceLevel = resource.resourceLevel, resourceQuantity = resource.quantity,
                grantedAt = user.Now, expiredAt = checked(user.Now + 30L * 24 * 60 * 60 * 1000), reason = reason
            });
        user.MarkChanged(nameof(SuiteUser.userPresents));
    }

    public List<UserPresentData> ReceivePresent(IEnumerable<string> presentIds)
    {
        var received = new List<UserPresentData>();
        foreach (var pid in presentIds)
        {
            var result = ReceiveOnePresent(pid);
            if (result != null)
                received.Add(result);
        }
        return received;
    }

    private UserPresentData? ReceiveOnePresent(string presentId)
    {
        if (user.Data.userPresents == null) return null;

        var idx = user.Data.userPresents.FindIndex(p => p.presentId == presentId);
        if (idx < 0) return null;

        var present = user.Data.userPresents[idx];
        user.Data.userPresents.RemoveAt(idx);

        resourceService.Grant(BuildResourceFromPresent(present));
        user.MarkChanged(nameof(SuiteUser.userPresents));

        // 两次官方练习券领取均返回重新计算的排序值及 30 天领取记录期限。
        if (present.resourceType == "practice_ticket" && present.resourceLevel == 0)
        {
            present.seq = long.MaxValue - user.Now;
            present.expiredAt = user.Now + 30L * 24 * 60 * 60 * 1000;
        }

        user.Private.PresentHistories.Add(new UserPresentHistoryData
        {
            presentId = present.presentId,
            seq = present.seq,
            resourceType = present.resourceType,
            resourceId = present.resourceId,
            resourceLevel = present.resourceLevel,
            resourceQuantity = present.resourceQuantity,
            receivedAt = user.Now,
            reason = present.reason
        });

        return present;
    }

    private static UserResource BuildResourceFromPresent(UserPresentData present) =>
        new()
        {
            resourceType = present.resourceType,
            resourceId = present.resourceId,
            resourceLevel = present.resourceLevel,
            quantity = present.resourceQuantity
        };

    public List<UserPresentHistoryData> GetPresentHistory()
    {
        return user.Private.PresentHistories;
    }
}
