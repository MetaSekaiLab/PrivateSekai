extern alias game;

using game::Sekai;
using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Music;

public sealed class MusicMyListController(UserOperation operations, UserSession user, MusicMyListService lists) : PrskController
{
    [HttpPut("api/user/{userId}/myList/{listNo}")]
    public IActionResult Save(long userId, int listNo, [FromBody] PutUserMusicMyListRequest request)
    {
        var status = 200;
        var encoded = operations.Execute(userId, () =>
        {
            status = lists.Save(listNo, request);
            return status == 200
                ? (object)new PutUserMusicMyListResponse { updatedResources = user.BuildRefresh() }
                : new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = "", ErrorMessage = "" };
        });
        Response.StatusCode = status;
        return Encoded(encoded);
    }

    [HttpPatch("api/user/{userId}/myList/{listNo}")]
    public IActionResult Reset(long userId, int listNo)
    {
        var status = 200;
        var encoded = operations.Execute(userId, () =>
        {
            status = lists.Reset(listNo);
            return status == 200
                ? (object)new PatchUserMusicMyListResponse { updatedResources = user.BuildRefresh() }
                : new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = "", ErrorMessage = "" };
        });
        Response.StatusCode = status;
        return Encoded(encoded);
    }
}
