extern alias game;

using game::Sekai;
#if !PRIVATESEKAI_EMBEDDED
using Microsoft.AspNetCore.Mvc;
#endif
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Live;

public sealed class MusicVideoController(UserOperation operations, UserSession user, MusicVideoService videos) : PrskController
{
    [HttpPost("api/user/{userId}/music-video/{musicId}")]
    [PrskEmptyErrorResponse]
    public IActionResult Record(long userId, int musicId, [FromBody] UserMusicVideoRequest request)
    {
        if (request.musicPlayStatus is not ("start" or "end") ||
            request.musicCategoryName is not ("mv" or "mv_2d" or "image" or "original"))
        {
            Response.StatusCode = 400;
            return Encoded([]);
        }
        return Encoded(operations.Execute(userId, () =>
        {
            var previous = BeginnerMissionResponse.AchievedIds(user.Data);
            videos.Record(musicId, request);
            var refresh = user.BuildRefresh();
            BeginnerMissionResponse.AddAchievementHints(refresh, previous);
            return new SuiteUserCommonResponse { updatedResources = refresh };
        }));
    }
}
