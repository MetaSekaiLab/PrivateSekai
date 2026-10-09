extern alias game;

using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Missions;

namespace PrivateSekai.Tests;

internal static class BeginnerMissionResponseChecks
{
    public static void Run()
    {
        var before = new SuiteUser
        {
            userMissionStatuses = [
                new() { missionType = "beginner_mission_v2", missionId = 11, missionStatus = "achieved" },
                new() { missionType = "beginner_mission_v2", missionId = 12, missionStatus = "received" },
                new() { missionType = "honor_mission", missionId = 13, missionStatus = "achieved" }]
        };
        var records = new[] { 11, 12, 13, 14, 15 }.Select(id => new UserBeginnerMissionV2
            { beginnerMissionV2Id = id, progress = 3 }).ToArray();
        var refresh = new SuiteUser
        {
            userBeginnerMissionV2s = records,
            userMissionStatuses = [.. before.userMissionStatuses,
                new() { missionType = "beginner_mission_v2", missionId = 13, missionStatus = "achieved" },
                new() { missionType = "beginner_mission_v2", missionId = 14, missionStatus = "achieved" }]
        };
        BeginnerMissionResponse.AddAchievementHints(refresh, BeginnerMissionResponse.AchievedIds(before));
        Check.That(refresh.userBeginnerMissionV2s.Where(m => m.isNewAchieved).Select(m => m.beginnerMissionV2Id)
            .SequenceEqual([13, 14]), "新手提示覆盖本次全部新达成，排除历史已达成、已领取及其他任务类型");
        Check.That(records.All(m => !m.isNewAchieved) && refresh.userBeginnerMissionV2s.All(m => m.progress == 3),
            "映射只复制提示，保留进度且不修改持久数组");
        var next = new SuiteUser { userBeginnerMissionV2s = records, userMissionStatuses = refresh.userMissionStatuses };
        BeginnerMissionResponse.AddAchievementHints(next, BeginnerMissionResponse.AchievedIds(refresh));
        Check.That(next.userBeginnerMissionV2s.All(m => !m.isNewAchieved), "下一次响应不重复提示已经达成的新手任务");
        var omitted = new SuiteUser { userMissionStatuses = refresh.userMissionStatuses };
        BeginnerMissionResponse.AddAchievementHints(omitted, BeginnerMissionResponse.AchievedIds(new SuiteUser()));
        Check.That(omitted.userBeginnerMissionV2s == null, "窄响应没有新手进度时不新增字段");
    }
}
