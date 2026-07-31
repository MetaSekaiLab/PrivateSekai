using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class MissionController : PrskController
{
    private readonly UserManager _users;

    public MissionController(UserManager users)
    {
        _users = users;
    }

    /// <summary>
    /// 领取 Beginner Mission V2 奖励。客户端提交 missionIds，成功后合并用户资源并展示获得奖励。
    /// </summary>
    [HttpPut("api/user/{userId}/mission/beginner_mission_v2")]
    public IActionResult HandleBeginnerMissionV2(
        long userId,
        [FromBody] UserMissionReceiveRequest request)
    {
        var user = _users.GetUser(userId);
        return Ok(user.ReceiveBeginnerMissionV2Rewards(request.missionIds));
    }
}
