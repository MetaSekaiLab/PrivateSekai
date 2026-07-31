using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Crypto;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class InheritController : PrskController
{
    private readonly UserManager _users;
    private readonly ILogger<InheritController> _logger;

    public InheritController(UserManager users, ILogger<InheritController> logger)
    {
        _users = users;
        _logger = logger;
    }

    /// <summary>
    /// 获取当前账号的设备转移限制信息。客户端在设置账号继承、绑定平台账号等继承相关入口前会先请求它，用返回结果决定确认弹窗中是否追加限制提示。
    /// </summary>
    [HttpGet("api/user/{userId}/restrict-info")]
    public IActionResult HandleRestrictInfo(long userId)
    {
        return Ok(new RestrictInfoResponse
        {
            isRestrictDeviceTransfer = false
        });
    }

    /// <summary>
    /// 设置 ID/password 引继码形式的账号继承信息。客户端提交用户输入的继承密码，服务端返回生成的 `inheritId` 和用户资源差异，客户端随后展示继承 ID 与密码给用户保存。
    /// </summary>
    [HttpPut("api/user/{userId}/inherit")]
    public IActionResult HandleSetInherit(long userId, [FromBody] UserInheritRequest request)
    {
        if (string.IsNullOrEmpty(request.password))
            return BadRequest("Missing password");

        var user = _users.GetUser(userId);
        var inheritId = user.SetUserInherit(request.password);

        _logger.LogInformation("User {UserId} set inherit ID {InheritId}", userId, inheritId);

        return Ok(new UserInheritSetResponse
        {
            updatedResources = user.GetRefreshData(),
            userInherit = new UserInherit { inheritId = inheritId }
        });
    }

    /// <summary>
    /// 使用 ID/password 引继码查询或执行账号继承。客户端第一次请求通常用于预览目标账号并打开确认弹窗；用户确认后会再次请求并执行继承，成功后用返回的 `credential` 切换本地账号。
    /// </summary>
    [HttpPost("api/inherit/user/{inheritId}")]
    public IActionResult HandleInheritUser(string inheritId)
    {
        var isExecuteInherit = Request.Query["isExecuteInherit"].ToString();
        if (string.IsNullOrEmpty(isExecuteInherit))
            return BadRequest("Missing isExecuteInherit parameter");

        if (isExecuteInherit != "True" && isExecuteInherit != "False")
            return BadRequest("Invalid isExecuteInherit parameter value");

        var inheritToken = Request.Headers["X-Inherit-Id-Verify-Token"].ToString();
        if (string.IsNullOrEmpty(inheritToken))
            return BadRequest("Missing X-Inherit-Id-Verify-Token header");

        var inheritData = JwtSignature.VerifyToken(inheritToken);
        if (inheritData == null
            || !inheritData.TryGetValue("inheritId", out var iid)
            || iid?.ToString() != inheritId)
        {
            return Unauthorized("Invalid inherit token or inherit ID mismatch");
        }

        if (!inheritData.TryGetValue("password", out var pwd) || pwd == null)
            return BadRequest("Missing password");

        var password = pwd.ToString()!;

        long? matchedUserId = null;
        GameUser? matchedUser = null;

        foreach (var (uid, user) in _users.GetAllUsers())
        {
            if (user.VerifyInherit(inheritId, password))
            {
                matchedUserId = uid;
                matchedUser = user;
                break;
            }
        }

        if (matchedUser == null || matchedUserId == null)
        {
            _logger.LogWarning("Inherit ID {InheritId} with provided password not found", inheritId);
            return Unauthorized("Invalid inherit ID or password");
        }

        _logger.LogInformation("User {UserId} inherited account with inherit ID {InheritId}",
            matchedUserId, inheritId);

        var responseData = new InheritExecuteResponse
        {
            afterUserGamedata = matchedUser.GetAfterUserGamedata(),
            userEventDeviceTransferRestrict = new RestrictInfoResponse
            {
                isRestrictDeviceTransfer = false
            }
        };

        if (isExecuteInherit == "True")
            responseData.credential = JwtSignature.GenUserCredential(matchedUserId.Value);

        return Ok(responseData);
    }
}
