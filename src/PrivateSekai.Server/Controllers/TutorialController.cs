using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class TutorialController : PrskController
{
    private readonly UserManager _users;
    private readonly ILogger<TutorialController> _logger;

    public TutorialController(UserManager users, ILogger<TutorialController> logger)
    {
        _users = users;
        _logger = logger;
    }

    /// <summary>
    /// PATCH /api/user/{userId}/tutorial
    /// </summary>
    [HttpPatch("api/user/{userId}/tutorial")]
    public IActionResult HandleTutorialUpdate(long userId, [FromBody] UserTutorialRequest request)
    {
        if (string.IsNullOrEmpty(request.tutorialStatus))
            return BadRequest("Missing tutorialStatus");

        _logger.LogInformation("User {UserId} updated tutorial status to `{Status}`", userId, request.tutorialStatus);

        var user = _users.GetUser(userId);
        user.UpdateTutorialProgress(request.tutorialStatus);

        return Ok(new SuiteUserCommonResponse
        {
            updatedResources = user.GetRefreshData()
        });
    }

    /// <summary>
    /// PATCH /api/user/{userId}
    /// </summary>
    [HttpPatch("api/user/{userId}")]
    public IActionResult HandleUserUpdate(long userId, [FromBody] UserNameRequest request)
    {
        var newName = request.userGamedata?.name;
        if (string.IsNullOrEmpty(newName))
            return BadRequest("Missing name in userGamedata");

        var user = _users.GetUser(userId);
        user.UpdateUserName(newName);
        user.UpdateRefreshableType(nameof(SuiteUser.userGamedata));

        _logger.LogInformation("User {UserId} gamedata updated", userId);

        return Ok(new SuiteUserCommonResponse
        {
            updatedResources = user.GetRefreshData()
        });
    }
}
