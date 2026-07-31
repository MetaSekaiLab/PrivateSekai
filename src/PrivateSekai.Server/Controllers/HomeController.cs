using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Crypto;
using PrivateSekai.Models;
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
    public IActionResult HandleUserHomeRefresh(long userId, [FromBody] HomeRefreshRequest? request)
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
            informations = user.Data.userInformations
        });
    }
}
