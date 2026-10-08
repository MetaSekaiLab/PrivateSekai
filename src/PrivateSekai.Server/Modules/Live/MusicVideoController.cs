extern alias game;

using System.Linq;
using game::Sekai;
#if !PRIVATESEKAI_EMBEDDED
using Microsoft.AspNetCore.Mvc;
#endif
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
            var previous = (user.Data.userMissionStatuses ?? []).Where(s => s.missionType == "beginner_mission_v2" &&
                s.missionStatus is "achieved" or "received").Select(s => s.missionId).ToHashSet();
            videos.Record(musicId, request);
            var refresh = user.BuildRefresh();
            if (refresh.userBeginnerMissionV2s != null)
            {
                var achieved = (user.Data.userMissionStatuses ?? []).Where(s => s.missionType == "beginner_mission_v2" &&
                    s.missionStatus == "achieved" && !previous.Contains(s.missionId)).Select(s => s.missionId).ToHashSet();
                refresh.userBeginnerMissionV2s = refresh.userBeginnerMissionV2s.Select(m => new UserBeginnerMissionV2
                {
                    beginnerMissionV2Id = m.beginnerMissionV2Id, progress = m.progress,
                    isNewAchieved = achieved.Contains(m.beginnerMissionV2Id)
                }).ToArray();
            }
            return new SuiteUserCommonResponse { updatedResources = refresh };
        }));
    }
}
