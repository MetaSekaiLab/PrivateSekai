using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class MiscController : PrskController
{
    private readonly UserManager _users;

    public MiscController(UserManager users)
    {
        _users = users;
    }

    /// <summary>
    /// 标记一个用户 topic 已读。客户端在登录后、功能解锁提示展示后、部分首页/Live/虚拟 Live 教程流程中，会把未读 topic 的 `topicId` 提交给服务端，成功后合并返回的用户资源差异。
    /// </summary>
    [HttpPut("api/user/{userId}/topic/{topicId}")]
    public IActionResult HandleTopic(long userId, int topicId)
    {
        var user = _users.GetUser(userId);
        user.RemoveTopic(topicId);

        return Ok(new TopicResponse
        {
            updatedResources = user.GetRefreshData()
        });
    }

    /// <summary>
    /// 标记一组 appeal 已读。客户端会根据 `MasterAppeal` 的目标类型和读取条件筛选需要提交的 appeal ID，展示过对应引导、提示或促销内容后，把 ID 列表提交给服务端，成功后合并返回的用户资源差异。
    /// </summary>
    [HttpPut("api/user/{userId}/appeal")]
    public IActionResult HandleAppeal(long userId, [FromBody] UserAppealRequest request)
    {
        var user = _users.GetUser(userId);
        user.MarkAppealsViewed(request.appealIds);

        return Ok(new SuiteUserCommonResponse
        {
            updatedResources = user.GetRefreshData()
        });
    }

    /// <summary>
    /// SNC
    /// </summary>
    [HttpPost("api/user/{userId}/{uuid}")]
    public IActionResult HandleUuidEndpoint(long userId, string uuid) => Ok();
}
