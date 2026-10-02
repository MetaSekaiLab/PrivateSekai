extern alias game;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using game::Sekai;
using game::Sekai.CustomProfile;
using PrivateSekai.Transport;
using PrivateSekai.Shared.Users;
using EmptyResponse = PrivateSekai.Models.EmptyResponse;

using PrivateSekai.Storage;

namespace PrivateSekai.Modules.Profiles;

public sealed class CustomProfileController(UserOperation operations, UserSession user, ProfileService profiles, CustomProfileThumbnailStore thumbnails, ILogger<CustomProfileController> logger) : PrskController
{
    /// <summary>
    /// 保存自定义名片的名称和卡片排序。客户端改名、重排卡片、或统一保存名片状态时都会走这个接口，成功后合并用户自定义名片相关的资源差异。
    /// </summary>
    [HttpPut("api/user/{userId}/custom-profile/{customProfileId}")]
    public IActionResult HandleSaveCustomProfile(
        long userId,
        int customProfileId,
        [FromBody] UserSaveCustomProfileRequest request)
    {
        return Encoded(operations.Execute(userId, () =>
        {
            profiles.SaveCustomProfile(customProfileId, request.name, request.customProfileCardOrders);

            return new SuiteUserCommonResponse
            {
                updatedResources = user.BuildRefresh()
            };
        }));
    }

    /// <summary>
    /// 创建新的自定义名片卡。客户端保存卡片时会先检查当前名片下是否已有目标 `customProfileCardId`；若不存在，就用 POST 创建，并上传缩略图数据和卡片内容。
    /// </summary>
    [HttpPost("api/user/{userId}/custom-profile/{customProfileId}/custom-profile-card/{customProfileCardId}")]
    public IActionResult HandleCreateCustomProfileCard(
        long userId,
        int customProfileId,
        int customProfileCardId,
        [FromBody] UserSaveCustomProfileCardRequest request)
    {
        return Encoded(operations.Execute(userId, () =>
        {
            profiles.SaveCustomProfileCard(customProfileId, customProfileCardId, request);

            return new SuiteUserCommonResponse
            {
                updatedResources = user.BuildRefresh()
            };
        }));
    }

    /// <summary>
    /// 更新已有自定义名片卡。客户端保存卡片时若当前名片下已存在目标 `customProfileCardId`，就用 PUT 覆盖保存缩略图数据和卡片内容。
    /// </summary>
    [HttpPut("api/user/{userId}/custom-profile/{customProfileId}/custom-profile-card/{customProfileCardId}")]
    public IActionResult HandleUpdateCustomProfileCard(
        long userId,
        int customProfileId,
        int customProfileCardId,
        [FromBody] UserSaveCustomProfileCardRequest request)
    {
        return Encoded(operations.Execute(userId, () =>
        {
            profiles.SaveCustomProfileCard(customProfileId, customProfileCardId, request);

            return new SuiteUserCommonResponse
            {
                updatedResources = user.BuildRefresh()
            };
        }));
    }

    /// <summary>
    /// 删除自定义名片。客户端当前确认的 UI 流程是单卡删除；API 封装同时支持多个 `customProfileCardId` query，因此服务端应按重复 query 处理。
    /// </summary>
    [HttpDelete("api/user/{userId}/custom-profile/{customProfileId}/custom-profile-card")]
    public IActionResult HandleDeleteCustomProfileCard(
        long userId,
        int customProfileId,
        [FromQuery] int[] customProfileCardId)
    {
        if (customProfileCardId.Length == 0)
            return BadRequest("Missing customProfileCardId");

        return Encoded(operations.Execute(userId, () =>
        {
            profiles.DeleteCustomProfileCards(customProfileId, customProfileCardId);

            return new SuiteUserCommonResponse
            {
                updatedResources = user.BuildRefresh()
            };
        }));
    }

    /// <summary>
    /// 下载自定义名片卡缩略图。客户端从 `userCustomProfileCards.thumbnailPath` 拿到图片路径后，拼出完整图片 URL，使用普通图片请求下载并缓存为 PNG。
    /// </summary>
    [PrskPlaintextResponse]
    [HttpGet("image/custom-profile-card/thumbnail/{hash}/{thumbnailId}")]
    public IActionResult HandleCustomProfileThumbnail(string hash, string thumbnailId)
    {
        if (!thumbnails.TryGetThumbnail(hash, thumbnailId, out var bytes, out var contentType))
            return NotFound("Thumbnail not found");

        return File(bytes, contentType);
    }

    /// <summary>
    /// 举报其他用户的自定义名片。客户端通过通用社区举报流程收集举报类型和当前位置，提交后只需要空响应来完成外层举报流程。
    /// </summary>
    [HttpPost("api/user/{userId}/report/{reportedUserId}/custom-profile/{customProfileId}")]
    public IActionResult HandleCustomProfileReport(
        long userId,
        long reportedUserId,
        int customProfileId,
        [FromBody] PostCustomProfileCommunityReportRequest request)
    {
        var reason = request.userReportReason;
        logger.LogInformation(
            "Custom profile report: reporter={ReporterUserId}, reported={ReportedUserId}, customProfileId={CustomProfileId}, reasonTypes={ReasonTypes}, location={Location}",
            userId,
            reportedUserId,
            customProfileId,
            reason?.userReportReasonTypes == null ? "" : string.Join(",", reason.userReportReasonTypes),
            reason?.userReportLocation ?? "");

        return Ok(new EmptyResponse());
    }
}
