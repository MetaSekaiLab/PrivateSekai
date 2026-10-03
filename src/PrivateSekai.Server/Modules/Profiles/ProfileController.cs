extern alias game;

using System.Linq;
using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Profiles;

public sealed class ProfileController(UserOperation operations, UserSession user, ProfileService profiles, CostumeService costumes) : PrskController
{
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
    public IActionResult HandleUserProfile(long userId, [FromBody] PutUserProfileRequest request)
    {
        return Encoded(operations.Execute(userId, () =>
        {
            profiles.UpdateProfile(new UserProfile
            {
                word = request.word,
                twitterId = request.twitterId,
                profileImageType = request.profileImageType,
                profileImageId = request.profileImageId ?? 0
            });

            return new SuiteUserCommonResponse
            {
                updatedResources = user.BuildRefresh()
            };
        }));
    }

}
