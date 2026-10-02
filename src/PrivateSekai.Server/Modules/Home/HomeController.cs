extern alias game;

using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Transport;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Home;

public sealed class HomeController(UserOperation operations, UserSession user, HomeService home) : PrskController
{
    /// <summary>
    /// PUT /api/user/{userId}/home/refresh
    /// </summary>
    [HttpPut("api/user/{userId}/home/refresh")]
    [PrskOptionalBody]
    public IActionResult HandleUserHomeRefresh(long userId, [FromBody] UserHomeRefreshRequest? request)
    {
        return Encoded(operations.Execute(userId, () =>
        {
            home.Refresh(request);

            return new SuiteUserCommonResponse
            {
                updatedResources = user.BuildRefresh()
            };
        }));
    }

    /// <summary>
    /// GET /api/information
    /// </summary>
    [HttpGet("api/information")]
    public IActionResult HandleInformation()
    {
        return Encoded(operations.Query(0, () =>
        {
            return new InformationResponse
            {
                informations = user.Data.userNews
            };
        }));
    }
}
