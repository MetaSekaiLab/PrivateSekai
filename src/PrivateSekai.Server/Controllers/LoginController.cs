using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Crypto;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class LoginController : PrskController
{
    private readonly UserManager _users;
    private readonly ILogger<LoginController> _logger;

    public LoginController(UserManager users, ILogger<LoginController> logger)
    {
        _users = users;
        _logger = logger;
    }

    /// <summary>
    /// 注册新用户账号。客户端在本地没有已保存账号时调用它，拿到新用户登记信息、后续认证用凭证，以及一份初始用户数据。
    /// </summary>
    [HttpPost("api/user")]
    public IActionResult HandleRegisterUser([FromBody] UserAuthRequest _)
    {
        var newUserId = _users.ForkNewUser();
        var user = _users.GetUser(newUserId);
        _logger.LogInformation("New user registered: ID={UserId}", newUserId);

        var suiteData = user.GetSuiteUserData();

        return Ok(new UserAPIResponse
        {
            userRegistration = suiteData.userRegistration,
            credential = JwtSignature.GenUserCredential(newUserId),
            updatedResources = suiteData
        });
    }
    
    /// <summary>
    /// 拉取指定用户的完整 `SuiteUser` 数据。客户端用它在登录后建立完整本地用户状态，也会在部分功能检测到本地状态可能过期时用它做全量同步。
    /// <param name="userId"></param>
    /// <returns>完整用户数据</returns>
    /// </summary>
    [HttpGet("api/suite/user/{userId}")]
    public IActionResult HandleSuiteUser(long userId)
    {
        var user = _users.UserExists(userId)
            ? _users.GetUser(userId)
            : _users.GetUser(0);

        return Ok(user.GetSuiteUserData());
    }

    /// <summary>
    /// 按 `name` 拉取指定用户数据片段。客户端当前确认用它刷新好友相关数据，返回仍是 `SuiteUser` 结构，但通常只需要包含请求的片段。
    /// </summary>
    [HttpGet("api/suite/user/{userId}/parts")]
    public IActionResult HandleSuiteUserParts(long userId, [FromQuery(Name = "name")] string[]? names)
    {
        var user = _users.UserExists(userId)
            ? _users.GetUser(userId)
            : _users.GetUser(0);

        return Ok(user.GetSuiteUserParts(names));
    }
}
