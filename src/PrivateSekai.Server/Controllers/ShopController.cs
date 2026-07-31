using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class ShopController : PrskController
{
    private readonly UserManager _users;

    public ShopController(UserManager users)
    {
        _users = users;
    }

    /// <summary>
    /// <p>[POST] 购买商店项目。客户端把 `shopId` 和 `shopItemId` 拼入 path，不发送请求体；成功后合并返回的用户资源差异，并由对应购买弹窗继续关闭、刷新或展示购买结果。</p>
    /// <p>[PUT] 更新已有区域商店项目，主要用于区域商店的升级/强化分支。它和购买接口使用同一路径，但 method 为 PUT；客户端同样不发送请求体，成功后合并用户资源差异。</p>
    /// </summary>
    [HttpPost("api/user/{userId}/shop/{shopId}/item/{shopItemId}")]
    [HttpPut("api/user/{userId}/shop/{shopId}/item/{shopItemId}")]
    public IActionResult HandleShopItemPurchase(long userId, int shopId, int shopItemId)
    {
        var user = _users.GetUser(userId);
        user.PurchaseShopItem(shopId, shopItemId);

        return Ok(new SuiteUserCommonResponse
        {
            updatedResources = user.GetRefreshData()
        });
    }
}
