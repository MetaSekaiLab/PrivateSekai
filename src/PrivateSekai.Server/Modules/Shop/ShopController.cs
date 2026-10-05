extern alias game;

using System.Linq;
using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Shop;

public sealed class ShopController(UserOperation operations, UserSession user, ShopService shop, MissionMasterQueries missionMaster) : PrskController
{
    [HttpPut("api/user/{userId}/event-exchange/{eventExchangeId}")]
    public IActionResult ExchangeEvent(long userId, int eventExchangeId, [FromQuery] int count)
    {
        var status = 200;
        var encoded = operations.Execute(userId, () =>
        {
            var result = shop.ExchangeEvent(eventExchangeId, count);
            status = result.Status;
            return status == 200 ? new UserEventExchangeResponse
            {
                obtainUserResources = result.Rewards, updatedResources = user.BuildRefresh()
            } : null;
        });
        return status == 200 ? Encoded(encoded) : StatusCode(status);
    }

    [HttpPut("api/user/{userId}/material-exchange/{materialExchangeId}")]
    public IActionResult ExchangeMaterial(long userId, int materialExchangeId, [FromQuery] int costGroupId, [FromQuery] int count = 1)
    {
        var status = 200;
        var encoded = operations.Execute(userId, () =>
        {
            status = shop.ExchangeMaterial(materialExchangeId, costGroupId, count);
            return status == 200 ? new UserExchangeResponse { updatedResources = user.BuildRefresh(), releasedActionSetIds = [] } : null;
        });
        return status == 200 ? Encoded(encoded) : StatusCode(status);
    }

    /// <summary>
    /// <p>[POST] 购买商店项目。客户端把 `shopId` 和 `shopItemId` 拼入 path，不发送请求体；成功后合并返回的用户资源差异，并由对应购买弹窗继续关闭、刷新或展示购买结果。</p>
    /// <p>[PUT] 更新已有区域商店项目，主要用于区域商店的升级/强化分支。它和购买接口使用同一路径，但 method 为 PUT；客户端同样不发送请求体，成功后合并用户资源差异。</p>
    /// </summary>
    [HttpPost("api/user/{userId}/shop/{shopId}/item/{shopItemId}")]
    [HttpPut("api/user/{userId}/shop/{shopId}/item/{shopItemId}")]
    public IActionResult HandleShopItemPurchase(long userId, int shopId, int shopItemId)
    {
        var status = 200;
        var encoded = operations.Execute(userId, () =>
        {
            var achievedBefore = (user.Data.userMissionStatuses ?? [])
                .Where(s => s.missionType == "beginner_mission_v2" && s.missionStatus is "achieved" or "received")
                .Select(s => s.missionId).ToHashSet();
            var result = shop.PurchaseShopItem(shopId, shopItemId);
            status = result.Status;
            if (status != 200)
                return new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = result.ErrorCode, ErrorMessage = "" };
            var refresh = user.BuildRefresh(result.ExcludeShop ? [nameof(SuiteUser.userShops)] : null);
            if (refresh.userBeginnerMissionV2s != null)
            {
                var newlyAchieved = (user.Data.userMissionStatuses ?? [])
                    .Where(s => s.missionType == "beginner_mission_v2" && s.missionStatus == "achieved" && !achievedBefore.Contains(s.missionId))
                    .Select(s => s.missionId).ToHashSet();
                refresh.userBeginnerMissionV2s = refresh.userBeginnerMissionV2s.Select(m => new UserBeginnerMissionV2
                {
                    beginnerMissionV2Id = m.beginnerMissionV2Id, progress = m.progress,
                    isNewAchieved = newlyAchieved.Contains(m.beginnerMissionV2Id)
                }).ToArray();
            }
            if (refresh.userCharacterMissions != null)
                refresh.userCharacterMissions = refresh.userCharacterMissions.Select(m => new UserCharacterMissionV2
                {
                    userId = m.userId, characterId = m.characterId, characterMissionType = m.characterMissionType,
                    progress = m.progress, achievedMissions = result.Achieved.Where(s => s.characterId == m.characterId &&
                        m.characterMissionType == missionMaster.GetCharacterMissionType(s.missionId)).ToArray()
                }).ToArray();

            return new SuiteUserCommonResponse
            {
                updatedResources = refresh
            };
        });
        Response.StatusCode = status;
        return Encoded(encoded);
    }
}
