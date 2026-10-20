extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Missions;

public sealed class LivePointResourceHandler(MissionMasterQueries master) : IResourceHandler
{
    public IReadOnlyCollection<string> ResourceTypes { get; } = ["live_point"];

    public void Grant(UserSession user, UserResource resource)
    {
        if (resource.quantity <= 0 || resource.resourceId != 0 || resource.resourceLevel != 0)
            throw new InvalidOperationException("Invalid Live point reward.");
        var periodId = master.GetLiveMissionPeriodId(user.Now);
        if (periodId <= 0) throw new InvalidOperationException("Missing current Live mission period.");
        var saved = user.Data.userLiveMissions?.SingleOrDefault(m => m.liveMissionPeriodId == periodId);
        if (saved != null && saved.liveMissionStatus != "free")
            throw new NotSupportedException("Live point rewards for a paid track are not verified.");
        LiveMissionProgress.Grant(user, master, periodId, resource.quantity);
    }
}
