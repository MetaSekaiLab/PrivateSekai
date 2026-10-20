extern alias game;

using System;
using System.Collections.Generic;
using game::Sekai;
using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;
using PrivateSekai.Transport.Realtime;

namespace PrivateSekai.Modules.Live;

public sealed class DiarkisController(UserOperation operations, DiarkisServer server, MultiLiveRooms rooms) : PrskController
{
    [HttpGet("api/user/{userId}/diarkis-auth")]
    public IActionResult Authenticate(long userId, [FromQuery] string diarkisServerType) => Respond(userId, () =>
    {
        if (diarkisServerType != "multi") throw new NotSupportedException("仅支持普通多人实时服务。");
        return server.Authenticate(userId);
    });

    [HttpPost("api/user/{userId}/diarkis-room/{roomId}/no")]
    public IActionResult Number(long userId, string roomId, [FromBody] PrivateRoomNumberRequest request) => Respond(userId, () =>
    {
        if (request.privateRoomType != "multi_live" || request.liveRuleType != "normal")
            throw new NotSupportedException("仅支持普通多人私人房间。");
        return rooms.Number(userId, roomId);
    });

    [HttpPatch("api/user/{userId}/diarkis-room/{roomId}")]
    public IActionResult Update(long userId, string roomId, [FromBody] PrivateRoomNumberPatchRequest request) => Respond(userId, () =>
    {
        if (request.liveRuleType != "normal" || request.parameter != null || !request.multiLiveLobbyId.HasValue)
            throw new NotSupportedException("仅支持指定大厅的普通私人房间。");
        return rooms.Update(userId, roomId, request.multiLiveLobbyId.Value);
    });

    [HttpGet("api/user/{userId}/diarkis-room-type/{privateRoomType}/no/{roomNo}")]
    public IActionResult Lookup(long userId, string privateRoomType, int roomNo) => Respond(userId, () =>
    {
        if (privateRoomType != "multi_live") throw new NotSupportedException("仅支持普通多人私人房间。");
        return rooms.Lookup(roomNo);
    });

    private IActionResult Respond(long userId, Func<byte[]> action)
    {
        if (!JwtSignature.VerifySessionToken(Request.Headers["X-Session-Token"].ToString(), userId)) return Error(401);
        if (operations.Read(userId) == null) return Error(404);
        try { return Encoded(action()); }
        catch (UnauthorizedAccessException) { return Error(403); }
        catch (KeyNotFoundException) { return Error(404); }
        catch (NotSupportedException) { return Error(501); }
        catch (RoomConflictException) { return Error(409); }
    }

    private IActionResult Error(int status)
    {
        Response.StatusCode = status;
        return Encoded(DumpSerializer.Serialize(new ClientErrorResponse
            { HttpStatus = (uint)status, ErrorCode = "", ErrorMessage = "" }));
    }
}
