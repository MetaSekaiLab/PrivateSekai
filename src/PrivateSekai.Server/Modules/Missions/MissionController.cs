extern alias game;

using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Missions;

public sealed class MissionController(UserOperation operations, UserSession user, MissionService missions) : PrskController
{
    [HttpPut("api/user/{userId}/mission/live_mission")]
    public IActionResult ReceiveLiveMission(long userId, [FromBody] UserMissionReceiveRequest request) =>
        Encoded(operations.Execute(userId, () => new UserMissionReceiveResponse
        {
            ObtainedRewards = missions.ReceiveLiveMissionRewards(request.missionIds),
            UpdatedResources = user.BuildRefresh()
        }));

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
