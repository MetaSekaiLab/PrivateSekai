extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Missions;

public sealed class MissionService(
    UserSession user,
    MissionMasterQueries master,
    ResourceMasterQueries resourceMaster,
    ResourceService resourceService)
{
    private const string BeginnerMissionV2Type = "beginner_mission_v2";

    public UserMissionReceiveResponse ReceiveBeginnerMissionV2Rewards(int[]? missionIds)
    {
        var obtainedRewards = new List<UserResource>();
        if (missionIds == null || missionIds.Length == 0)
        {
            return new UserMissionReceiveResponse
            {
                ObtainedRewards = []
            };
        }

        foreach (var missionId in missionIds.Where(id => id > 0).Distinct())
        {
            MarkMissionReceived(BeginnerMissionV2Type, missionId);
            var mission = master.GetBeginnerMissionV2(missionId);
            foreach (var reward in mission?.rewards ?? [])
            {
                var resources = resourceMaster.BuildResourcesFromBox("mission_reward", reward.resourceBoxId);
                foreach (var resource in resources)
                {
                    resourceService.Grant(resource);
                    obtainedRewards.Add(resource);
                }
            }
        }

        user.MarkChanged(nameof(SuiteUser.userBeginnerMissionV2s));
        user.MarkChanged(nameof(SuiteUser.userMissionStatuses));

        return new UserMissionReceiveResponse
        {
            ObtainedRewards = obtainedRewards.ToArray()
        };
    }

    public void TouchBeginnerMissionProgress(int missionId)
    {
        user.Data.userBeginnerMissionV2s ??= [];
        var missions = user.Data.userBeginnerMissionV2s.ToList();
        var mission = missions.FirstOrDefault(m => m.beginnerMissionV2Id == missionId);
        if (mission == null)
        {
            mission = new UserBeginnerMissionV2
            {
                beginnerMissionV2Id = missionId
            };
            missions.Add(mission);
        }

        var requirement = master.GetBeginnerMissionV2(missionId)?.requirement ?? 1;
        mission.progress = Math.Max(mission.progress, requirement);
        mission.isNewAchieved = true;
        user.Data.userBeginnerMissionV2s = missions.OrderBy(m => m.beginnerMissionV2Id).ToArray();
        MarkMissionAchieved(BeginnerMissionV2Type, missionId);
        user.MarkChanged(nameof(SuiteUser.userBeginnerMissionV2s));
        user.MarkChanged(nameof(SuiteUser.userMissionStatuses));
    }

    private void MarkMissionAchieved(string missionType, int missionId)
    {
        var status = GetOrCreateMissionStatus(missionType, missionId);
        if (!string.Equals(status.missionStatus, "received", StringComparison.Ordinal))
            status.missionStatus = "achieved";
    }

    private void MarkMissionReceived(string missionType, int missionId)
    {
        var status = GetOrCreateMissionStatus(missionType, missionId);
        status.missionStatus = "received";

        if (string.Equals(missionType, BeginnerMissionV2Type, StringComparison.Ordinal))
        {
            user.Data.userBeginnerMissionV2s ??= [];
            var missions = user.Data.userBeginnerMissionV2s.ToList();
            var mission = missions.FirstOrDefault(m => m.beginnerMissionV2Id == missionId);
            if (mission == null)
            {
                mission = new UserBeginnerMissionV2
                {
                    beginnerMissionV2Id = missionId
                };
                missions.Add(mission);
            }

            var requirement = master.GetBeginnerMissionV2(missionId)?.requirement ?? 1;
            mission.progress = Math.Max(mission.progress, requirement);
            mission.isNewAchieved = false;
            user.Data.userBeginnerMissionV2s = missions.OrderBy(m => m.beginnerMissionV2Id).ToArray();
        }
    }

    private UserMissionStatus GetOrCreateMissionStatus(string missionType, int missionId)
    {
        user.Data.userMissionStatuses ??= [];
        var statuses = user.Data.userMissionStatuses.ToList();
        var status = statuses.FirstOrDefault(s =>
            s.missionId == missionId &&
            string.Equals(s.missionType, missionType, StringComparison.Ordinal));

        if (status != null)
            return status;

        status = new UserMissionStatus
        {
            userId = user.UserId,
            missionType = missionType,
            missionId = missionId,
            missionStatus = "achieved"
        };
        statuses.Add(status);
        user.Data.userMissionStatuses = statuses
            .OrderBy(s => s.missionType, StringComparer.Ordinal)
            .ThenBy(s => s.missionId)
            .ToArray();
        return status;
    }

    public void UpdateLiveMissionProgress(UserLivePoint livePoint)
    {
        if (livePoint.liveMissionPeriodId <= 0 || livePoint.addNormalProgress <= 0)
            return;

        user.Data.userLiveMissions ??= [];
        var missions = user.Data.userLiveMissions.ToList();
        var mission = missions.FirstOrDefault(m =>
            m.liveMissionPeriodId == livePoint.liveMissionPeriodId &&
            string.Equals(m.liveMissionStatus, "free", StringComparison.Ordinal));

        if (mission == null)
        {
            mission = new UserLiveMission
            {
                userId = user.UserId,
                liveMissionPeriodId = livePoint.liveMissionPeriodId,
                liveMissionStatus = "free",
                achievedMissionIds = []
            };
            missions.Add(mission);
        }

        mission.progress += livePoint.addNormalProgress;
        mission.achievedMissionIds ??= [];
        user.Data.userLiveMissions = missions.ToArray();
        user.MarkChanged(nameof(SuiteUser.userLiveMissions));
    }
}
