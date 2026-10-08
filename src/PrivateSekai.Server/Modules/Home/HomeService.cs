extern alias game;

using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Home;

public sealed class HomeService(UserSession user)
{
    public void SetLoginStatus(string status) => user.Private.LoginStatus = new UserLoginStatus
    {
        loginStatus = status, loginStatusUpdatedAt = user.Now
    };

    private static readonly Dictionary<int, int[]> FixedShopActionSetsByArea = new()
    {
        [3] = [4, 384, 2002, 2005],
        [4] = [3, 838, 2001, 2006]
    };

    public void Refresh(UserHomeRefreshRequest? request)
    {
        if (request?.refreshableTypes?.Contains("lottery_action_set") == true)
            RefreshAreaActionSets();

        user.MarkChanged(nameof(SuiteUser.userFriends));
    }

    public void MarkAppealsViewed(int[]? appealIds)
    {
        if (appealIds == null || appealIds.Length == 0)
            return;

        var mergedIds = (user.Data.viewableAppeal?.appealIds ?? [])
            .Concat(appealIds.Where(id => id > 0))
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        user.Data.viewableAppeal = new ViewableAppeal
        {
            appealIds = mergedIds
        };
        user.MarkChanged(nameof(SuiteUser.viewableAppeal));
    }

    public void RefreshAreaActionSets()
    {
        EnsureShopAreaActionSets();
        user.MarkChanged(nameof(SuiteUser.userAreas));
    }

    public void EnsureShopAreaActionSets()
    {
        if (user.Data.userAreas == null)
            return;

        foreach (var (areaId, actionSetIds) in FixedShopActionSetsByArea)
        {
            var area = user.Data.userAreas.FirstOrDefault(a => a.areaId == areaId);
            if (area == null)
                continue;

            area.userAreaStatus ??= new UserAreaStatus
            {
                areaId = areaId
            };
            area.userAreaStatus.status = UserAreaStatus.AREA_STATUS_RELEASED;

            var actionSets = (area.actionSets ?? []).ToList();
            foreach (var actionSetId in actionSetIds)
            {
                if (actionSets.Any(a => a.id == actionSetId))
                    continue;

                actionSets.Add(new UserActionSet
                {
                    id = actionSetId,
                    status = UserActionSet.ACTION_SET_STATUS_UNREAD
                });
            }

            area.actionSets = actionSets.ToArray();
        }
    }

    public void RemoveTopic(int topicId)
    {
        if (user.Data.unreadUserTopics == null) return;
        user.Data.unreadUserTopics = user.Data.unreadUserTopics
            .Where(t => t.topicId != topicId).ToArray();
        user.MarkChanged(nameof(SuiteUser.unreadUserTopics));
    }
}
