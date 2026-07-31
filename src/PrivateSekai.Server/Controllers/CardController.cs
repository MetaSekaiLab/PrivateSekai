using Microsoft.AspNetCore.Mvc;
using MessagePack;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class CardController : PrskController
{
    private readonly UserManager _users;

    public CardController(UserManager users)
    {
        _users = users;
    }

    /// <summary>
    /// 执行等待室卡牌转换。客户端把确认转换的卡牌汇总成 `userCards` 发给服务端，请求成功后合并返回的用户资源差异，并刷新等待室显示。
    /// </summary>
    [HttpPut("api/user/{userId}/card")]
    public IActionResult HandleCard(long userId, [FromQuery] string? behavior, [FromBody] UserCardExchangeRequest request)
    {
        if (!string.Equals(behavior, "exchange", StringComparison.Ordinal))
            return NotFound();

        var user = _users.GetUser(userId);
        user.ExchangeCards(request.userCards);

        return Ok(new UserCardExchangeResponse
        {
            updatedResources = user.GetRefreshData()
        });
    }

    /// <summary>
    /// 使用练习券提升卡牌等级。客户端把目标卡牌写入 path，并提交本次消耗的练习券列表；成功后使用返回的经验变化结果播放练习结果。
    /// </summary>
    [HttpPost("api/user/{userId}/card/{cardId}/practice-ticket")]
    public IActionResult HandleCardPracticeTicket(
        long userId,
        int cardId,
        [FromBody] UserCardPracticeTicketRequest request)
    {
        var user = _users.GetUser(userId);
        return Ok(user.PracticeCardWithTickets(cardId, request.costs));
    }

    /// <summary>
    /// 执行卡牌 Master Lesson。客户端提交本次消耗的 Master Lesson cost ID，成功后合并用户资源并展示获得奖励。
    /// </summary>
    [HttpPost("api/user/{userId}/card/{cardId}/master-lesson")]
    public IActionResult HandleCardMasterLesson(
        long userId,
        int cardId,
        [FromBody] UserCardMasterLessonRequest request)
    {
        var user = _users.GetUser(userId);
        return Ok(user.MasterLessonCard(cardId, request.masterLessonCostIds));
    }

    /// <summary>
    /// 更新单张卡牌的培养相关状态。当前确认的行为包括特训完成状态和默认显示立绘。
    /// </summary>
    [HttpPut("api/user/{userId}/card/{cardId}")]
    public async Task<IActionResult> HandleCardBehavior(
        long userId,
        int cardId,
        [FromQuery] string? behavior)
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        var request = ms.ToArray();

        var user = _users.GetUser(userId);
        switch (behavior)
        {
            case "special_training":
                var specialTrainingRequest =
                    MessagePackSerializer.Deserialize<UserCardSpecialTrainingRequest>(request);
                user.SetCardSpecialTrainingStatus(cardId, specialTrainingRequest?.specialTrainingStatus);
                break;
            case "set_default_image":
                var defaultImageRequest =
                    MessagePackSerializer.Deserialize<UserCardDefaultImageRequest>(request);
                user.SetCardDefaultImage(cardId, defaultImageRequest?.defaultImage);
                break;
            default:
                return NotFound();
        }

        return Ok(new SuiteUserCommonResponse
        {
            updatedResources = user.GetRefreshData()
        });
    }
}
