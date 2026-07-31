using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class PresentController : PrskController
{
    private readonly UserManager _users;

    public PresentController(UserManager users)
    {
        _users = users;
    }

    /// <summary>
    /// 获取当前用户的礼物领取历史。客户端进入礼物邮箱内容时会拉取历史记录，并和本地已有的 `userPresents` 一起构建礼物列表和历史页。
    /// </summary>
    [HttpGet("api/user/{userId}/present/history")]
    public IActionResult HandlePresentHistory(long userId)
    {
        var user = _users.GetUser(userId);
        return Ok(new UserPresentHistoriesResponse
        {
            userPresentHistories = user.GetPresentHistory()
        });
    }

    /// <summary>
    /// 领取一个或多个礼物。客户端把要领取的 `presentIds` 提交给服务端，成功后合并返回的用户资源差异，并用 `receivedUserPresents` 展示奖励结果。
    /// </summary>
    [HttpPost("api/user/{userId}/present")]
    public IActionResult HandleReceivePresent(long userId, [FromBody] UserPresentRequest request)
    {
        var user = _users.GetUser(userId);
        var received = user.ReceivePresent(request.presentIds ?? []);

        return Ok(new UserPresentReceiveResponse
        {
            updatedResources = user.GetRefreshData(),
            receivedUserPresents = received
        });
    }
}
