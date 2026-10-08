extern alias game;

using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Missions;

public sealed class MissionController(UserOperation operations, UserSession user, MissionService missions) : PrskController
{
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
        var status = 200;
        var bytes = operations.Execute(userId, () =>
        {
            var result = missions.ReceiveHonorMissionRewards(request.missionIds);
            status = result.Status;
            return status == 200
                ? (object)new UserMissionReceiveResponse { ObtainedRewards = result.Rewards, UpdatedResources = user.BuildRefresh() }
                : new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = "", ErrorMessage = "" };
        });
        Response.StatusCode = status;
        return Encoded(bytes);
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
                var response = missions.ReceiveBeginnerMissionV2Rewards(request.missionIds);
                response.UpdatedResources = user.BuildRefresh();
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
}
