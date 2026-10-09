extern alias game;

using System.Collections.Generic;
using System.Linq;
using game::Sekai;

namespace PrivateSekai.Modules.Missions;

public static class HonorMissionResponse
{
    public static HashSet<int> AchievedIds(SuiteUser data) => (data.userMissionStatuses ?? [])
        .Where(s => s.missionType == "honor_mission" && s.missionStatus is "achieved" or "received")
        .Select(s => s.missionId).ToHashSet();

    // 只映射响应副本，本次达成提示不写入用户状态。
    public static void AddAchievementHints(SuiteUser refresh, ISet<int> previous, MissionMasterQueries master)
    {
        if (refresh.userHonorMissions == null) return;
        var newlyAchieved = (refresh.userMissionStatuses ?? [])
            .Where(s => s.missionType == "honor_mission" && s.missionStatus == "achieved" && !previous.Contains(s.missionId))
            .Select(s => master.GetHonorMission(s.missionId)!).ToArray();
        if (newlyAchieved.Length == 0) return;
        refresh.userHonorMissions = refresh.userHonorMissions.Select(m => new UserHonorMission
        {
            honorMissionType = m.honorMissionType, progress = m.progress,
            achievedMissionIds = newlyAchieved.Where(d => d.honorMissionType == m.honorMissionType).Select(d => d.id).ToArray()
        }).ToArray();
    }
}
