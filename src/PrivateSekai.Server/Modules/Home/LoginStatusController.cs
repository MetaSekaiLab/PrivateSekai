extern alias game;

using System;
using game::Sekai;
#if !PRIVATESEKAI_EMBEDDED
using Microsoft.AspNetCore.Mvc;
#endif
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Home;

public sealed class LoginStatusController(UserOperation operations, HomeService home) : PrskController
{
    [HttpPut("api/user/{userId}/login-status")]
    [PrskEmptyErrorResponse]
    public IActionResult Save(long userId, [FromBody] PutUserLoginStatusRequest request)
    {
        if (request.loginStatus == null || !Enum.IsDefined(typeof(LoginStatus), request.loginStatus))
        {
            Response.StatusCode = 400;
            return Encoded([]);
        }
        return Encoded(operations.Execute(userId, () =>
        {
            home.SetLoginStatus(request.loginStatus);
            return new Models.EmptyResponse();
        }));
    }
}
