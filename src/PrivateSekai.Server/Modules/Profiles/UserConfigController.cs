extern alias game;

using game::Sekai;
#if !PRIVATESEKAI_EMBEDDED
using Microsoft.AspNetCore.Mvc;
#endif
using PrivateSekai.Models;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Profiles;

public sealed class UserConfigController(UserOperation operations, UserSession user, ProfileService profiles) : PrskController
{
    [HttpPost("api/user/{userId}/config")]
    [PrskEmptyErrorResponse]
    public IActionResult Save(long userId, [FromBody] PostUserConfigRequest request)
    {
        if (request.defaultMusicType is not (null or "sekai" or "original_music") ||
            request.friendRequestScope is not (null or "all" or "id_search" or "reject"))
        {
            Response.StatusCode = 400;
            return Encoded([]);
        }
        return Encoded(operations.Execute(userId, () =>
        {
            profiles.SaveConfig(request);
            return new UserConfigResponse { UpdatedResources = user.BuildRefresh(), Config = user.Data.userConfig };
        }));
    }
}
