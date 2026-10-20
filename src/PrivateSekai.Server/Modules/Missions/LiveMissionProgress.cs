extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Missions;

internal static class LiveMissionProgress
{
    public static void Grant(UserSession user, MissionMasterQueries master, int periodId, int quantity)
    {
        if (periodId <= 0 || quantity <= 0) throw new ArgumentException("Invalid Live mission progress.");
        var missions = (user.Data.userLiveMissions ?? []).ToList();
        var mission = missions.SingleOrDefault(m => m.liveMissionPeriodId == periodId && m.liveMissionStatus == "free");
        if (mission == null)
        {
            mission = new UserLiveMission { userId = user.UserId, liveMissionPeriodId = periodId,
                liveMissionStatus = "free", achievedMissionIds = [] };
            missions.Add(mission);
        }
        if (mission.progress < 0) throw new InvalidOperationException("Invalid saved Live mission progress.");
        mission.progress = checked(mission.progress + quantity);
        mission.achievedMissionIds ??= [];
        user.Data.userLiveMissions = missions.ToArray();
        user.MarkChanged(nameof(SuiteUser.userLiveMissions));
        var statuses = (user.Data.userMissionStatuses ?? []).ToList();
        var changed = false;
        foreach (var definition in master.GetFreeLiveMissions(periodId))
        {
            if (mission.progress < definition.requirement) continue;
            var status = statuses.SingleOrDefault(s => s.missionType == "live_mission" && s.missionId == definition.id);
            if (status?.missionStatus is "achieved" or "received") continue;
            if (status == null)
            {
                status = new UserMissionStatus { userId = user.UserId, missionType = "live_mission", missionId = definition.id };
                statuses.Add(status);
            }
            status.missionStatus = "achieved";
            changed = true;
        }
        if (changed)
        {
            user.Data.userMissionStatuses = statuses.OrderBy(s => s.missionType, StringComparer.Ordinal).ThenBy(s => s.missionId).ToArray();
            user.MarkChanged(nameof(SuiteUser.userMissionStatuses));
        }
    }
}
