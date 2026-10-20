extern alias game;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using game::Sekai;
using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Protocol;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Live;

public sealed class MultiLiveController(UserOperation operations, UserSession user, MultiLiveService live, MultiLiveRooms rooms,
    MissionMasterQueries missions) : PrskController
{
    [HttpPost("api/multi-live")]
    public IActionResult Prepare([FromBody] MultiLiveRequest request) => Respond(request.userId, () =>
        rooms.PrepareOwner(request.userId, request, liveId => operations.Execute(request.userId, () =>
        {
            live.Prepare(liveId, request, new UserMultiLiveRequest
            {
                photonRoomName = request.photonRoomName, privateFlg = request.privateFlg, multiLiveLobbyId = request.multiLiveLobbyId,
                musicId = request.musicId, musicDifficultyId = request.musicDifficultyId, musicVocalId = request.musicVocalId,
                deckId = request.deckId, boostCount = request.boostCount, musicCategoryName = "image",
                privateRoomSettings = request.privateRoomSettings, customMusicScoreId = request.customMusicScoreId
            });
            return new MultiLiveResponse
            {
                liveId = liveId, isInBreakTime = false,
                updatedResources = new() { userEventBreakTime = user.Data.userEventBreakTime }
            };
        })));

    [HttpPost("api/user/{userId}/multi-live/{liveId}")]
    public IActionResult PrepareMember(long userId, string liveId, [FromBody] UserMultiLiveRequest request) => Respond(userId, () =>
        rooms.PrepareMember(userId, liveId, request, all => operations.Execute(userId, () =>
        {
            live.Prepare(liveId, all, request);
            return new UserMultiLiveResponse
            {
                isInBreakTime = false, updatedResources = new() { userEventBreakTime = user.Data.userEventBreakTime }
            };
        })));

    [HttpPut("api/user/{userId}/multi-live/{liveId}")]
    public IActionResult Clear(long userId, string liveId, [FromBody] UserMultiLiveClearRequest request) => Respond(userId, () =>
        operations.Execute(userId, () =>
        {
            var previousBeginner = BeginnerMissionResponse.AchievedIds(user.Data);
            var previousHonor = HonorMissionResponse.AchievedIds(user.Data);
            var previousLive = (user.Data.userMissionStatuses ?? []).Where(s => s.missionType == "live_mission"
                && s.missionStatus is "achieved" or "received").Select(s => s.missionId).ToHashSet();
            var response = live.Clear(liveId, request);
            response.updatedResources = user.BuildRefresh();
            HonorMissionResponse.AddAchievementHints(response.updatedResources, previousHonor, missions);
            BeginnerMissionResponse.AddAchievementHints(response.updatedResources, previousBeginner);
            var achieved = (user.Data.userMissionStatuses ?? []).Where(s => s.missionType == "live_mission"
                && s.missionStatus == "achieved" && !previousLive.Contains(s.missionId)).Select(s => s.missionId).ToArray();
            if (achieved.Length > 0)
                response.updatedResources.userLiveMissions = response.updatedResources.userLiveMissions.Select(m => new UserLiveMission
                {
                    userId = m.userId, liveMissionPeriodId = m.liveMissionPeriodId, liveMissionStatus = m.liveMissionStatus,
                    progress = m.progress, paidProgress = m.paidProgress,
                    achievedMissionIds = m.liveMissionPeriodId == response.userLivePoint.liveMissionPeriodId ? achieved : m.achievedMissionIds
                }).ToArray();
            return response;
        }));

    private IActionResult Respond(long userId, Func<byte[]> action)
    {
        if (!JwtSignature.VerifySessionToken(Request.Headers["X-Session-Token"].ToString(), userId)) return Error(401);
        if (operations.Read(userId) == null) return Error(404);
        try { return Encoded(action()); }
        catch (MultiLiveRequestException error) { return Error(error.Status, error.ErrorCode); }
        catch (UnauthorizedAccessException) { return Error(403); }
        catch (KeyNotFoundException) { return Error(404); }
        catch (InvalidDataException) { return Error(400); }
        catch (RoomConflictException) { return Error(409); }
    }

    private IActionResult Error(int status, string code = "")
    {
        Response.StatusCode = status;
        return Encoded(DumpSerializer.Serialize(new ClientErrorResponse
            { HttpStatus = (uint)status, ErrorCode = code, ErrorMessage = "" }));
    }
}
