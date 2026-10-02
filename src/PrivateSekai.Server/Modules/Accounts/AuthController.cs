extern alias game;

using System;
using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Transport;
using PrivateSekai.Modules.Home;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Accounts;

public sealed class AuthController(
    AccountTemplates templates,
    TimeProvider clock,
    UserOperation operations,
    UserSession user,
    HomeService home) : PrskController
{
    [HttpPut("api/user/{userId}/auth")]
    public IActionResult HandleAuthUser(long userId, [FromBody] UserAuthRequest request)
    {
        if (string.IsNullOrEmpty(request.credential))
            return BadRequest("Missing credential");
        if (!JwtSignature.VerifyCredential(request.credential, userId))
            return Unauthorized("Invalid credential");
        return Encoded(operations.Restore(userId, templates.CreateUser, () =>
        {
            home.EnsureShopAreaActionSets();
            user.NormalizeEventBreakTime();
            return templates.GetAuth(JwtSignature.GenSessionToken(userId));
        }));
    }

    [HttpGet("api/system")]
    public IActionResult HandleSystemInfo() => Ok(templates.GetSystem(clock.GetUtcNow().ToUnixTimeMilliseconds()));
}
