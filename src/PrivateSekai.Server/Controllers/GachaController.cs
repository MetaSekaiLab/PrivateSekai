using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class GachaController : PrskController
{
    private readonly UserManager _users;

    public GachaController(UserManager users)
    {
        _users = users;
    }

    /// <summary>
    /// 检查指定功能模块是否处于维护中。客户端在进入可维护功能前会先请求该接口，用返回结果决定是否继续进入目标界面或走维护提示流程。
    /// TODO: 移动到独立的 Controller
    /// </summary>
    [HttpGet("api/module-maintenance/{kind}")]
    public IActionResult HandleModuleMaintenance(string kind)
    {
        return Ok(new ModuleMaintenanceResponse
        {
            moduleMaintenanceType = kind.ToLowerInvariant(),
            isOngoing = false
        });
    }

    /// <summary>
    /// 执行一次抽卡行为。客户端根据抽卡按钮、资源消耗方式、bonus 奖励选择和剩余抽取次数选择不同 query 变体；成功后合并返回的用户资源差异，并用完整抽卡结果驱动动画和结果页。
    /// </summary>
    [HttpPut("api/user/{userId:long}/gacha/{gachaId:int}/gachaBehaviorId/{gachaBehaviorId:int}")]
    public IActionResult HandleUserGacha(
        long userId,
        int gachaId,
        int gachaBehaviorId,
        [FromQuery] bool isPriorityUsePaidJewel = false)
    {
        var user = _users.GetUser(userId);
        return Ok(user.ExecuteGacha(gachaId, gachaBehaviorId, isPriorityUsePaidJewel));
    }

    /// <summary>
    /// 执行抽卡天井道具兑换。客户端在抽卡页头部或抽卡兑换页确认兑换后提交兑换配置，成功后合并用户资源差异，并展示获得资源、服装或饰品等结果。
    /// TODO: 移动到商店/交换相关 Controller
    /// </summary>
    [HttpPut("api/user/{userId:long}/exchange/gacha-ceil-item")]
    public IActionResult HandleGachaCeilItemExchange(long userId, [FromBody] UserGachaCeilExchangeRequest request)
    {
        var user = _users.GetUser(userId);
        return Ok(user.ExchangeGachaCeilItem(request));
    }

    /// <summary>
    /// 保存 Rate Choice 抽卡的愿望选择。客户端在卡牌选择页确认选择后提交当前选择列表，成功后合并用户资源差异并继续选择完成流程。
    /// </summary>
    [HttpPut("api/user/{userId:long}/rate-choice-gacha-wish")]
    public IActionResult HandleRateChoiceGachaWish(long userId, [FromBody] UserRateChoiceGachaWishRequest request)
    {
        var user = _users.GetUser(userId);
        user.SaveRateChoiceGachaWish(request);

        return Ok(new UserRateChoiceGachaWishResponse
        {
            updatedResources = user.GetRefreshData()
        });
    }
}
