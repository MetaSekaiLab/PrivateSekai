extern alias game;

using System.Linq;
using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Live;

public sealed class LiveController(UserOperation operations, UserSession user, LiveService live, MissionMasterQueries missions) : PrskController
{
    [HttpPost("api/user/{userId}/boost-item")]
    public IActionResult RecoverBoost(long userId, [FromBody] UserBoostItemRequest request)
    {
        var status = 200;
        var encoded = operations.Execute(userId, () =>
        {
            status = live.RecoverBoost(request);
            return status == 200 ? new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() } : null;
        });
        return status == 200 ? Encoded(encoded) : StatusCode(status);
    }

    /// <summary>
    /// 开始一次普通单人 Live。客户端在最终确认页提交歌曲、难度、队伍、消耗 boost、是否 Auto 等信息，成功后拿到 `userLiveId` 和演出中需要的技能/切入数据，再进入实际 Live。
    /// </summary>
    [HttpPost("api/user/{userId}/live")]
    public IActionResult HandleUserLiveStart(long userId, [FromBody] UserLiveRequest request)
    {
        var status = 200;
        var bytes = operations.Execute(userId, () =>
        {
            var result = live.StartUserLive(request);
            status = result.Status;
            if (status != 200)
                return (object)new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = result.ErrorCode, ErrorMessage = "" };
            var response = result.Response!;
            response.updatedResources = new UpdatedResources { userEventBreakTime = user.Data.userEventBreakTime };
            return response;
        });
        Response.StatusCode = status;
        return Encoded(bytes);
    }

    /// <summary>
    /// 提交普通单人 Live 结算结果。客户端在 Live 结束进入结果页后提交分数、判定数、最大连击、生命值、镜像设置和已播放切入语音组，成功后合并用户资源并用响应驱动结果页奖励、经验、活动点和成就显示。
    /// </summary>
    [HttpPut("api/user/{userId}/live/{userLiveId}")]
    public IActionResult HandleUserLiveClear(long userId, string userLiveId, [FromBody] UserLiveClearRequest request)
    {
        return Encoded(operations.Execute(userId, () =>
        {
            var previous = (user.Data.userMissionStatuses ?? [])
                .Where(s => s.missionType == "live_mission" && s.missionStatus is "achieved" or "received")
                .Select(s => s.missionId).ToHashSet();
            var previousBeginner = BeginnerMissionResponse.AchievedIds(user.Data);
            var previousHonor = HonorMissionResponse.AchievedIds(user.Data);
            var response = live.ClearUserLive(userLiveId, request);
            response.updatedResources = user.BuildRefresh();
            HonorMissionResponse.AddAchievementHints(response.updatedResources, previousHonor, missions);
            var achieved = (user.Data.userMissionStatuses ?? [])
                .Where(s => s.missionType == "live_mission" && s.missionStatus == "achieved" && !previous.Contains(s.missionId))
                .Select(s => s.missionId).ToArray();
            // 新达成 ID 仅用于本次结算展示，后续 Suite 不保留该列表。
            if (achieved.Length > 0)
                response.updatedResources.userLiveMissions = response.updatedResources.userLiveMissions.Select(m => new UserLiveMission
                {
                    userId = m.userId, liveMissionPeriodId = m.liveMissionPeriodId,
                    liveMissionStatus = m.liveMissionStatus, progress = m.progress, paidProgress = m.paidProgress,
                    achievedMissionIds = m.liveMissionPeriodId == response.userLivePoint.liveMissionPeriodId ? achieved : m.achievedMissionIds
                }).ToArray();
            BeginnerMissionResponse.AddAchievementHints(response.updatedResources, previousBeginner);
            return response;
        }));
    }

    /// <summary>
    /// 领取或标记 Live 结果相关的角色档案语音。客户端提交语音组 ID、Live 类型和 `userLiveId`，成功后通过返回的资源差异更新角色档案语音持有/已读状态。
    /// </summary>
    [HttpPost("api/user/{userId}/live-character-archive-voice/live-result")]
    public IActionResult HandleUserLiveCharacterArchiveVoiceLiveResult(
        long userId,
        [FromBody] UserLiveCharacterArchiveVoiceLiveResultRequest request)
    {
        return Encoded(operations.Execute(userId, () =>
        {
            live.ReceiveLiveCharacterArchiveVoiceResult(request.liveResultCharacterArchiveVoiceGroupId);

            return new UserLiveCharacterArchiveVoiceLiveResultResponse
            {
                updatedResources = user.BuildRefresh()
            };
        }));
    }
}
