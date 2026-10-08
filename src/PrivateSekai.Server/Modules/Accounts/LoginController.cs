extern alias game;

using System.Linq;
using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Transport;
using PrivateSekai.Modules.Home;
using PrivateSekai.Modules.Live;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Accounts;

public sealed class LoginController(
    UserOperation operations,
    UserSession user,
    AccountTemplates templates,
    HomeService home,
    BoostService boosts) : PrskController
{
    [HttpPost("api/user")]
    public IActionResult HandleRegisterUser([FromBody] UserAuthRequest _) =>
        Encoded(operations.Create(templates.CreateUser, () =>
        {
            home.EnsureShopAreaActionSets();
            return new UserAPIResponse
            {
                userRegistration = user.Data.userRegistration,
                credential = JwtSignature.GenUserCredential(user.UserId),
                updatedResources = user.BuildSuite()
            };
        }));

    [HttpGet("api/suite/user/{userId}")]
    [ServiceFilter(typeof(LoginBonusStatusFilter))]
    public IActionResult HandleSuiteUser(long userId) =>
        Encoded(operations.Execute(ResolveUser(userId), () =>
        {
            home.SetLoginStatus("online");
            boosts.Normalize();
            home.EnsureShopAreaActionSets();
            return WithFriendLoginStatuses(user.BuildSuite());
        }));

    [HttpGet("api/suite/user/{userId}/parts")]
    public IActionResult HandleSuiteUserParts(long userId, [FromQuery(Name = "name")] string[]? names) =>
        Encoded(operations.Execute(ResolveUser(userId), () =>
        {
            home.SetLoginStatus("online");
            return WithFriendLoginStatuses(user.BuildParts(names));
        }));

    private SuiteUser WithFriendLoginStatuses(SuiteUser result)
    {
        foreach (var friend in result.userFriends ?? [])
        {
            var opponent = operations.Read(friend.opponentUserId);
            if (opponent?.Data.userConfig?.isDisplayLoginStatus == false)
                friend.userLoginStatus = new UserLoginStatus { loginStatus = "offline" };
            else if (opponent?.Private.LoginStatus is { } status)
                friend.userLoginStatus = status;
        }
        return result;
    }

    // 保留登录拉取接口对缺失用户返回模板的既有行为。
    private long ResolveUser(long userId) => operations.GetUserIds().Contains(userId) ? userId : 0;
}
