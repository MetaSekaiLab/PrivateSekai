using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class ProfileController : PrskController
{
    private readonly UserManager _users;

    public ProfileController(UserManager users)
    {
        _users = users;
    }

    /// <summary>
    /// 更新玩家个人资料中的留言、Twitter ID 和头像显示信息。客户端在个人资料页离开或保存时检测到资料字段变化后提交，成功后合并返回的用户资源差异。
    /// </summary>
    [HttpPut("api/user/{userId}/profile")]
    public IActionResult HandleUserProfile(long userId, [FromBody] UserProfileRequest request)
    {
        var user = _users.GetUser(userId);
        user.UpdateProfile(new UserProfile
        {
            word = request.word,
            twitterId = request.twitterId,
            profileImageType = request.profileImageType,
            profileImageId = request.profileImageId ?? 0
        });

        return Ok(new SuiteUserCommonResponse
        {
            updatedResources = user.GetRefreshData()
        });
    }

}
