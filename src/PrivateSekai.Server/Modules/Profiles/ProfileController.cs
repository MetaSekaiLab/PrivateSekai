extern alias game;

using System.Linq;
using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Profiles;

public sealed class ProfileController(UserOperation operations, UserSession user, ProfileService profiles, CostumeService costumes,
    MissionMasterQueries missionMaster) : PrskController
{
    [HttpPatch("api/user/{userId}/stamp-favorite")]
    public IActionResult SaveStampFavorites(long userId, [FromBody] UserStampFavoriteRequest request) =>
        Encoded(operations.Execute(userId, () =>
        {
            profiles.SaveStampFavorites(request);
            return new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() };
        }));

    [HttpPost("api/user/{userId}/costume-3d-shop/{shopItemId}")]
    public IActionResult CraftCostume(long userId, int shopItemId) => Encoded(operations.Execute(userId, () =>
    {
        var previous = (user.Data.userMissionStatuses ?? []).Where(s => s.missionType == "beginner_mission_v2" &&
            s.missionStatus is "achieved" or "received").Select(s => s.missionId).ToHashSet();
        var previousHonor = HonorMissionResponse.AchievedIds(user.Data);
        var result = costumes.Craft(shopItemId);
        var refresh = user.BuildRefresh();
        HonorMissionResponse.AddAchievementHints(refresh, previousHonor, missionMaster);
        if (refresh.userBeginnerMissionV2s != null)
        {
            var achieved = (user.Data.userMissionStatuses ?? []).Where(s => s.missionType == "beginner_mission_v2" &&
                s.missionStatus == "achieved" && !previous.Contains(s.missionId)).Select(s => s.missionId).ToHashSet();
            refresh.userBeginnerMissionV2s = refresh.userBeginnerMissionV2s.Select(m => new UserBeginnerMissionV2
            {
                beginnerMissionV2Id = m.beginnerMissionV2Id, progress = m.progress,
                isNewAchieved = achieved.Contains(m.beginnerMissionV2Id)
            }).ToArray();
        }
        if (refresh.userCharacterMissions != null)
            refresh.userCharacterMissions = refresh.userCharacterMissions.Select(m => new UserCharacterMissionV2
            {
                userId = m.userId, characterId = m.characterId, characterMissionType = m.characterMissionType, progress = m.progress,
                achievedMissions = m.characterMissionType == "collect_costume_3d"
                    ? result.Achieved.Where(s => s.characterId == m.characterId).ToArray() : m.achievedMissions
            }).ToArray();
        return new UserCostume3DShopResponse { consumedCosts = result.Costs, obtainedResources = result.Rewards, updatedResources = refresh };
    }));

    [HttpPut("api/user/{userId}/character-costume-3d/character/{characterId}/unit/{unit}")]
    public IActionResult SaveCostume(long userId, int characterId, string unit, [FromBody] UserCharacterCostume3DRequest request) =>
        Encoded(operations.Execute(userId, () =>
        {
            var previous = (user.Data.userMissionStatuses ?? []).Where(s => s.missionType == "beginner_mission_v2" &&
                s.missionStatus is "achieved" or "received").Select(s => s.missionId).ToHashSet();
            costumes.Save(characterId, unit, request);
            var refresh = user.BuildRefresh();
            if (refresh.userBeginnerMissionV2s != null)
            {
                var achieved = (user.Data.userMissionStatuses ?? []).Where(s => s.missionType == "beginner_mission_v2" &&
                    s.missionStatus == "achieved" && !previous.Contains(s.missionId)).Select(s => s.missionId).ToHashSet();
                refresh.userBeginnerMissionV2s = refresh.userBeginnerMissionV2s.Select(m => new UserBeginnerMissionV2
                {
                    beginnerMissionV2Id = m.beginnerMissionV2Id, progress = m.progress,
                    isNewAchieved = achieved.Contains(m.beginnerMissionV2Id)
                }).ToArray();
            }
            return new SuiteUserCommonResponse { updatedResources = refresh };
        }));

    /// <summary>
    /// 更新玩家个人资料中的留言、Twitter ID 和头像显示信息。客户端在个人资料页离开或保存时检测到资料字段变化后提交，成功后合并返回的用户资源差异。
    /// </summary>
    [HttpPut("api/user/{userId}/profile")]
    [PrskEmptyErrorResponse]
    public IActionResult HandleUserProfile(long userId, [FromBody] PutUserProfileRequest request)
    {
        if (request.profileImageType is not ("leader" or "card_before_special_training" or "card_after_special_training"))
        {
            Response.StatusCode = 400;
            return Encoded([]);
        }
        var status = 200;
        var bytes = operations.Execute(userId, () =>
        {
            var result = profiles.UpdateProfile(request);
            status = result.Status;
            return status == 200
                ? (object)new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() }
                : new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = result.ErrorCode, ErrorMessage = "" };
        });
        Response.StatusCode = status;
        return Encoded(bytes);
    }

}
