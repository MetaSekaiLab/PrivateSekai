extern alias game;

using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Transport;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Accounts;

public sealed class InheritController(UserOperation operations, UserSession user, InheritService inherit) : PrskController
{
    [HttpGet("api/user/{userId}/restrict-info")]
    public IActionResult HandleRestrictInfo(long userId) => Ok(new UserRestrictInfo { isRestrictDeviceTransfer = false });

    [HttpPut("api/user/{userId}/inherit")]
    public IActionResult HandleSetInherit(long userId, [FromBody] UserIPassInheritRequest request)
    {
        if (string.IsNullOrEmpty(request.password))
            return BadRequest("Missing password");
        return Encoded(operations.Execute(userId, () =>
        {
            var id = inherit.SetUserInherit(request.password);
            return new UserIPassInheritResponse
            {
                updatedResources = user.BuildRefresh(),
                userInherit = new UserInherit { inheritId = id }
            };
        }));
    }

    [HttpPost("api/inherit/user/{inheritId}")]
    public IActionResult HandleInheritUser(string inheritId)
    {
        var execute = Request.Query["isExecuteInherit"].ToString();
        if (string.IsNullOrEmpty(execute))
            return BadRequest("Missing isExecuteInherit parameter");
        if (execute is not ("True" or "False"))
            return BadRequest("Invalid isExecuteInherit parameter value");
        var token = Request.Headers["X-Inherit-Id-Verify-Token"].ToString();
        if (string.IsNullOrEmpty(token))
            return BadRequest("Missing X-Inherit-Id-Verify-Token header");
        var data = JwtSignature.VerifyToken(token);
        if (data == null || !data.TryGetValue("inheritId", out var id) || id?.ToString() != inheritId)
            return Unauthorized("Invalid inherit token or inherit ID mismatch");
        if (!data.TryGetValue("password", out var password) || password == null)
            return BadRequest("Missing password");

        foreach (var userId in operations.GetUserIds())
        {
            var snapshot = operations.Read(userId)!;
            if (!InheritService.Matches(snapshot, inheritId, password.ToString()!))
                continue;
            var response = new PlatformInheritResponse
            {
                afterUserGamedata = InheritService.GetPreview(snapshot),
                userEventDeviceTransferRestrict = new UserRestrictInfo { isRestrictDeviceTransfer = false }
            };
            if (execute == "True")
                response.credential = JwtSignature.GenUserCredential(userId);
            return Ok(response);
        }
        return Unauthorized("Invalid inherit ID or password");
    }
}
