extern alias game;

using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Profiles;

public sealed class ProfileController(UserOperation operations, UserSession user, ProfileService profiles) : PrskController
{
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
