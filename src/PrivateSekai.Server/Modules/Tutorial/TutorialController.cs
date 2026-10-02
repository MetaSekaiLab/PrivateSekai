extern alias game;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using game::Sekai;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

using PrivateSekai.Modules.Profiles;

namespace PrivateSekai.Modules.Tutorial;

public sealed class TutorialController(UserOperation operations, UserSession user, TutorialService tutorial, ProfileService profiles, ILogger<TutorialController> logger) : PrskController
{
    /// <summary>
    /// PATCH /api/user/{userId}/tutorial
    /// </summary>
    [HttpPatch("api/user/{userId}/tutorial")]
    public IActionResult HandleTutorialUpdate(long userId, [FromBody] UserTutorialRequest request)
    {
        if (string.IsNullOrEmpty(request.tutorialStatus))
            return BadRequest("Missing tutorialStatus");

        logger.LogInformation("User {UserId} updated tutorial status to `{Status}`", userId, request.tutorialStatus);

        return Encoded(operations.Execute(userId, () =>
        {
            tutorial.UpdateTutorialProgress(request.tutorialStatus);

            return new SuiteUserCommonResponse
            {
                updatedResources = user.BuildRefresh()
            };
        }));
    }

    /// <summary>
    /// PATCH /api/user/{userId}
    /// </summary>
    [HttpPatch("api/user/{userId}")]
    public IActionResult HandleUserUpdate(long userId, [FromBody] UserNameAPIRequest request)
    {
        var newName = request.userGamedata?.name;
        if (string.IsNullOrEmpty(newName))
            return BadRequest("Missing name in userGamedata");

        return Encoded(operations.Execute(userId, () =>
        {
            profiles.UpdateUserName(newName);

            logger.LogInformation("User {UserId} gamedata updated", userId);

            return new SuiteUserCommonResponse
            {
                updatedResources = user.BuildRefresh()
            };
        }));
    }
}
