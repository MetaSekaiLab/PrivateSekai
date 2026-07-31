using MessagePack;
using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Crypto;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class AuthController : PrskController
{
    private readonly UserManager _users;
    private readonly ILogger<AuthController> _logger;

    public AuthController(UserManager users, ILogger<AuthController> logger)
    {
        _users = users;
        _logger = logger;
    }

    /// <summary>
    /// 认证已有账号并刷新客户端会话。客户端用本地保存的凭证和设备信息换取新的 `sessionToken`，同时接收版本信息、资源差异、规约状态、封禁信息和后续 master 加载所需路径。
    /// </summary>
    [HttpPut("api/user/{userId}/auth")]
    public IActionResult HandleAuthUser(long userId, [FromBody] UserAuthRequest request)
    {
        if (string.IsNullOrEmpty(request.credential))
            return BadRequest("Missing credential");

        if (!JwtSignature.VerifyCredential(request.credential, userId))
        {
            _logger.LogError("Invalid credential for user {UserId}", userId);
            return Unauthorized("Invalid credential");
        }

        _logger.LogInformation("User {UserId} authenticated", userId);

        var responseData = UserManager.GetApiUserAuth(JwtSignature.GenSessionToken(userId));
        
        return Ok(responseData);
    }

    /// <summary>
    /// 游戏客户端确认系统状态和版本信息
    /// </summary>
    [HttpGet("api/system")]
    public IActionResult HandleSystemInfo()
    {
        var responseData = UserManager.GetApiSystem();
        return Ok(responseData);
    }
}
