extern alias game;

using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Models;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Presents;

public sealed class PresentController(UserOperation operations, UserSession user, PresentService presents) : PrskController
{
    /// <summary>
    /// 获取当前用户的礼物领取历史。客户端进入礼物邮箱内容时会拉取历史记录，并和本地已有的 `userPresents` 一起构建礼物列表和历史页。
    /// </summary>
    [HttpGet("api/user/{userId}/present/history")]
    public IActionResult HandlePresentHistory(long userId)
    {
        return Encoded(operations.Query(userId, () =>
        {
            return new UserPresentHistoriesResponse
            {
                UserPresentHistories = presents.GetPresentHistory()
            };
        }));
    }

    /// <summary>
    /// 领取一个或多个礼物。客户端把要领取的 `presentIds` 提交给服务端，成功后合并返回的用户资源差异，并用 `receivedUserPresents` 展示奖励结果。
    /// </summary>
    [HttpPost("api/user/{userId}/present")]
    public IActionResult HandleReceivePresent(long userId, [FromBody] UserPresentAPIRequest request)
    {
        return Encoded(operations.Execute(userId, () =>
        {
            var received = presents.ReceivePresent(request.presentIds ?? []);

            return new PresentReceiptResponse(new UserPresentReceiveResponse
            {
                updatedResources = user.BuildRefresh(),
                receivedUserPresents = received
            }, user.Now);
        }));
    }
}
