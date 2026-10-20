extern alias game;

using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Missions;

public sealed class MissionController(UserOperation operations, UserSession user, MissionService missions, MissionMasterQueries master) : PrskController
{
    [HttpPost("api/user/{userId}/stamp-use-history")]
    [PrskEmptyErrorResponse]
    public IActionResult RecordStampUse(long userId, [FromBody] UserStampUseHistoryRequest request)
    {
        try
        {
            return Encoded(operations.Execute(userId, () =>
            {
                var previous = HonorMissionResponse.AchievedIds(user.Data);
                var status = missions.RecordStampUse(request);
                if (status != 200) throw new MissionRequestRejectedException(status);
                var refresh = user.BuildRefresh();
                HonorMissionResponse.AddAchievementHints(refresh, previous, master);
                return new SuiteUserCommonResponse { updatedResources = refresh };
            }));
        }
        catch (MissionRequestRejectedException error)
        {
            Response.StatusCode = error.Status;
            return Encoded(error.Status == 500 ? [] : operations.Query(userId, () =>
                new ClientErrorResponse { HttpStatus = (uint)error.Status, ErrorCode = "", ErrorMessage = "" }));
        }
    }

    [HttpPut("api/user/{userId}/character/{characterId}/mission/{characterMissionType}")]
    [HttpPut("api/user/{userId}/character/{characterId}/mission")]
    public IActionResult ReceiveCharacterMission(long userId, int characterId, string? characterMissionType = null) =>
        Encoded(operations.Execute(userId, () => new UserCharacterMissionV2Response
        {
            reportedMissionStatuses = missions.ReceiveCharacterMissions(characterId, characterMissionType),
            updatedResources = user.BuildRefresh()
        }));

    [HttpPut("api/user/{userId}/mission/live_mission")]
    public IActionResult ReceiveLiveMission(long userId, [FromBody] UserMissionReceiveRequest request) =>
        Encoded(operations.Execute(userId, () => new UserMissionReceiveResponse
        {
            ObtainedRewards = missions.ReceiveLiveMissionRewards(request.missionIds),
            UpdatedResources = user.BuildRefresh()
        }));

    [HttpPut("api/user/{userId}/mission/honor_mission")]
    public IActionResult ReceiveHonorMission(long userId, [FromBody] UserMissionReceiveRequest request)
    {
        try
        {
            return Encoded(operations.Execute(userId, () =>
            {
                var result = missions.ReceiveHonorMissionRewards(request.missionIds);
                if (result.Status != 200) throw new MissionRequestRejectedException(result.Status);
                return new UserMissionReceiveResponse { ObtainedRewards = result.Rewards, UpdatedResources = user.BuildRefresh() };
            }));
        }
        catch (MissionRequestRejectedException error)
        {
            Response.StatusCode = error.Status;
            return Encoded(operations.Query(userId, () =>
                new ClientErrorResponse { HttpStatus = (uint)error.Status, ErrorCode = "", ErrorMessage = "" }));
        }
    }

    [HttpPut("api/user/{userId}/mission/event_mission")]
    public IActionResult ReceiveEventMission(long userId, [FromBody] UserMissionReceiveRequest request)
    {
        try
        {
            return Encoded(operations.Execute(userId, () =>
            {
                var previous = (user.Data.userMissionStatuses ?? []).Where(s => s.missionType == "live_mission" &&
                    s.missionStatus is "achieved" or "received").Select(s => s.missionId).ToHashSet();
                var result = missions.ReceiveEventMissionRewards(request);
                if (result.Status != 200) throw new MissionRequestRejectedException(result.Status);
                var refresh = user.BuildRefresh();
                var achieved = (refresh.userMissionStatuses ?? []).Where(s => s.missionType == "live_mission" &&
                    s.missionStatus == "achieved" && !previous.Contains(s.missionId)).Select(s => master.GetLiveMission(s.missionId)
                        ?? throw new InvalidOperationException("Missing achieved Live mission.")).ToArray();
                if (refresh.userLiveMissions != null)
                    refresh.userLiveMissions = refresh.userLiveMissions.Select(m => new UserLiveMission
                    {
                        userId = m.userId, liveMissionPeriodId = m.liveMissionPeriodId, liveMissionStatus = m.liveMissionStatus, progress = m.progress,
                        achievedMissionIds = achieved.Where(d => d.liveMissionPeriodId == m.liveMissionPeriodId && d.liveMissionType == m.liveMissionStatus)
                            .Select(d => d.id).ToArray()
                    }).ToArray();
                return new UserMissionReceiveResponse { ObtainedRewards = result.Rewards, UpdatedResources = refresh };
            }));
        }
        catch (MissionRequestRejectedException error)
        {
            Response.StatusCode = error.Status;
            return Encoded(operations.Query(userId, () =>
                new ClientErrorResponse { HttpStatus = (uint)error.Status, ErrorCode = "", ErrorMessage = "" }));
        }
    }

    /// <summary>
    /// 领取 Beginner Mission V2 奖励。客户端提交 missionIds，成功后合并用户资源并展示获得奖励。
    /// </summary>
    [HttpPut("api/user/{userId}/mission/beginner_mission_v2")]
    public IActionResult HandleBeginnerMissionV2(
        long userId,
        [FromBody] UserMissionReceiveRequest request)
    {
        try
        {
            return Encoded(operations.Execute(userId, () =>
            {
                var previous = BeginnerMissionResponse.AchievedIds(user.Data);
                var response = missions.ReceiveBeginnerMissionV2Rewards(request.missionIds);
                response.UpdatedResources = user.BuildRefresh();
                BeginnerMissionResponse.AddAchievementHints(response.UpdatedResources, previous);
                return response;
            }));
        }
        catch (MissionAlreadyReceivedException)
        {
            Response.StatusCode = 409;
            return Encoded(operations.Query(userId, () =>
                new ClientErrorResponse { HttpStatus = 409, ErrorCode = "", ErrorMessage = "" }));
        }
    }

    private sealed class MissionRequestRejectedException(int status) : Exception
    {
        public int Status { get; } = status;
    }
}
