extern alias game;

using System.Collections.Generic;
using System.Linq;
using game::Sekai;

namespace PrivateSekai.Modules.Missions;

public static class BeginnerMissionResponse
{
    public static HashSet<int> AchievedIds(SuiteUser data) => (data.userMissionStatuses ?? [])
        .Where(s => s.missionType == "beginner_mission_v2" && s.missionStatus is "achieved" or "received")
        .Select(s => s.missionId).ToHashSet();

    // 替换响应数组，达成提示不写入持久状态。
    public static void AddAchievementHints(SuiteUser refresh, ISet<int> previous)
    {
        if (refresh.userBeginnerMissionV2s == null) return;
        var achieved = (refresh.userMissionStatuses ?? [])
            .Where(s => s.missionType == "beginner_mission_v2" && s.missionStatus == "achieved" && !previous.Contains(s.missionId))
            .Select(s => s.missionId).ToHashSet();
        refresh.userBeginnerMissionV2s = refresh.userBeginnerMissionV2s.Select(m => new UserBeginnerMissionV2
        {
            beginnerMissionV2Id = m.beginnerMissionV2Id, progress = m.progress,
            isNewAchieved = achieved.Contains(m.beginnerMissionV2Id)
        }).ToArray();
    }
}
