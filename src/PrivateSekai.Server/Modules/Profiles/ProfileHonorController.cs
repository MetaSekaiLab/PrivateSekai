extern alias game;

using game::Sekai;
#if !PRIVATESEKAI_EMBEDDED
using Microsoft.AspNetCore.Mvc;
#endif
using PrivateSekai.Modules.Missions;
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
            var previous = BeginnerMissionResponse.AchievedIds(user.Data);
            status = honors.Save(request);
            if (status != 200)
                return (object)new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = "", ErrorMessage = "" };
            var refresh = user.BuildRefresh();
            BeginnerMissionResponse.AddAchievementHints(refresh, previous);
            return new SuiteUserCommonResponse { updatedResources = refresh };
        });
        Response.StatusCode = status;
        return Encoded(encoded);
    }
}
