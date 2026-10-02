extern alias game;

using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Crypto;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class HomeController : PrskController
{
    private readonly UserManager _users;

    public HomeController(UserManager users)
    {
        _users = users;
    }

    /// <summary>
    /// PUT /api/user/{userId}/home/refresh
    /// </summary>
    [HttpPut("api/user/{userId}/home/refresh")]
    [PrskOptionalBody]
    public IActionResult HandleUserHomeRefresh(long userId, [FromBody] UserHomeRefreshRequest? request)
    {
        var user = _users.GetUser(userId);

        if (request?.refreshableTypes?.Contains("lottery_action_set") == true)
            user.RefreshAreaActionSets();

        user.UpdateRefreshableType(nameof(SuiteUser.userFriends));

        return Ok(new SuiteUserCommonResponse
        {
            updatedResources = user.GetRefreshData()
        });
    }

    /// <summary>
    /// GET /api/information
    /// </summary>
    [HttpGet("api/information")]
    public IActionResult HandleInformation()
    {
        var user = _users.GetUser(0);
        return Ok(new InformationResponse
        {
            informations = user.Data.userNews
        });
    }
}
