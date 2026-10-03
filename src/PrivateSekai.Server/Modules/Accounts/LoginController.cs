extern alias game;

using System.Linq;
using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Transport;
using PrivateSekai.Modules.Home;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Accounts;

public sealed class LoginController(
    UserOperation operations,
    UserSession user,
    AccountTemplates templates,
    HomeService home) : PrskController
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
        Encoded(operations.Query(ResolveUser(userId), () =>
        {
            home.EnsureShopAreaActionSets();
            return user.BuildSuite();
        }));

    [HttpGet("api/suite/user/{userId}/parts")]
    public IActionResult HandleSuiteUserParts(long userId, [FromQuery(Name = "name")] string[]? names) =>
        Encoded(operations.Query(ResolveUser(userId), () => user.BuildParts(names)));

    // 保留登录拉取接口对缺失用户返回模板的既有行为。
    private long ResolveUser(long userId) => operations.GetUserIds().Contains(userId) ? userId : 0;
}
