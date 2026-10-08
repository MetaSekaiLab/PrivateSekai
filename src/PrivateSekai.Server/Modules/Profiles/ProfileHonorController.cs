extern alias game;

using game::Sekai;
#if !PRIVATESEKAI_EMBEDDED
using Microsoft.AspNetCore.Mvc;
#endif
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Profiles;

public sealed class ProfileHonorController(UserOperation operations, UserSession user, ProfileHonorService honors) : PrskController
{
    [HttpPut("api/user/{userId}/profile-honor")]
    public IActionResult Save(long userId, [FromBody] PutUserProfileHonorRequest request)
    {
        var status = 200;
        var encoded = operations.Execute(userId, () =>
        {
            status = honors.Save(request);
            return status == 200
                ? (object)new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() }
                : new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = "", ErrorMessage = "" };
        });
        Response.StatusCode = status;
        return Encoded(encoded);
    }
}
