using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class LiveController : PrskController
{
    private readonly UserManager _users;

    public LiveController(UserManager users)
    {
        _users = users;
    }

    /// <summary>
    /// 开始一次普通单人 Live。客户端在最终确认页提交歌曲、难度、队伍、消耗 boost、是否 Auto 等信息，成功后拿到 `userLiveId` 和演出中需要的技能/切入数据，再进入实际 Live。
    /// </summary>
    [HttpPost("api/user/{userId}/live")]
    public IActionResult HandleUserLiveStart(long userId, [FromBody] UserLiveRequest request)
    {
        var user = _users.GetUser(userId);
        return Ok(user.StartUserLive(request));
    }

    /// <summary>
    /// 提交普通单人 Live 结算结果。客户端在 Live 结束进入结果页后提交分数、判定数、最大连击、生命值、镜像设置和已播放切入语音组，成功后合并用户资源并用响应驱动结果页奖励、经验、活动点和成就显示。
    /// </summary>
    [HttpPut("api/user/{userId}/live/{userLiveId}")]
    public IActionResult HandleUserLiveClear(long userId, string userLiveId, [FromBody] UserLiveClearRequest request)
    {
        var user = _users.GetUser(userId);
        return Ok(user.ClearUserLive(userLiveId, request));
    }

    /// <summary>
    /// 领取或标记 Live 结果相关的角色档案语音。客户端提交语音组 ID、Live 类型和 `userLiveId`，成功后通过返回的资源差异更新角色档案语音持有/已读状态。
    /// </summary>
    [HttpPost("api/user/{userId}/live-character-archive-voice/live-result")]
    public IActionResult HandleUserLiveCharacterArchiveVoiceLiveResult(
        long userId,
        [FromBody] UserLiveCharacterArchiveVoiceLiveResultRequest request)
    {
        var user = _users.GetUser(userId);
        user.ReceiveLiveCharacterArchiveVoiceResult(request.liveResultCharacterArchiveVoiceGroupId);

        return Ok(new UserLiveCharacterArchiveVoiceLiveResultResponse
        {
            updatedResources = user.GetRefreshData()
        });
    }
}
