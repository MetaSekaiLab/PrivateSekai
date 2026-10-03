extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Presents;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Home;

public sealed class LoginBonusService(UserSession user, LoginBonusMasterQueries master,
    ResourceMasterQueries resources, PresentService presents, MissionService missions)
{
    public UserLoginBonus[] ClaimInitial()
    {
        user.Data.userGamedata.lastLoginAt = user.Now;
        user.MarkChanged(nameof(SuiteUser.userGamedata));
        // 后续日次及跨日重置仍待核验；已有记录不会再次发放首日奖励。
        if (user.Data.userLoginBonuses?.Length > 0) return [];
        var rewards = master.InitialRewards(user.Now);
        missions.RecordInitialLogin();
        var claimed = rewards.Select(r => new UserLoginBonus
        {
            userId = user.UserId, loginBonusType = r.Type, loginBonusId = r.Id, progress = 1,
            receivedAt = user.Now, displayTexts = []
        }).ToArray();
        foreach (var reward in rewards)
        {
            var contents = resources.BuildResourcesFromBox("login_bonus", reward.BoxId);
            if (contents.Length == 0) throw new InvalidOperationException("Missing login reward box.");
            presents.AddLoginRewards(contents, reward.Reason);
        }
        user.Data.userLoginBonuses = claimed.OrderBy(b => b.loginBonusType, StringComparer.Ordinal)
            .ThenBy(b => b.loginBonusId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userLoginBonuses));
        return claimed;
    }
}
