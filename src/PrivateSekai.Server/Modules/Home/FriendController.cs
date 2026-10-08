extern alias game;

using game::Sekai;
#if !PRIVATESEKAI_EMBEDDED
using Microsoft.AspNetCore.Mvc;
#endif
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Home;

public sealed class FriendController(UserOperation operations, UserSession user, FriendService friends, FriendQueries queries) : PrskController
{
    [HttpPost("api/user/{userId}/friend/{opponentUserId}")]
    public IActionResult SendRequest(long userId, long opponentUserId, [FromBody] PostUserFriendRequest request) =>
        Apply(userId, opponentUserId, peer => friends.Request(peer, request));

    [HttpPut("api/user/{userId}/friend/{opponentUserId}")]
    public IActionResult Approve(long userId, long opponentUserId) => Apply(userId, opponentUserId, peer => (friends.Approve(peer), ""));

    [HttpDelete("api/user/{userId}/friend/{opponentUserId}")]
    public IActionResult Remove(long userId, long opponentUserId, [FromQuery] string type) =>
        Apply(userId, opponentUserId, peer => (friends.Remove(peer, type), ""));

    private IActionResult Apply(long userId, long opponentUserId, System.Func<UserState, (int Status, string ErrorCode)> action)
    {
        var status = 200;
        var bytes = operations.ExecutePair(userId, opponentUserId, peer =>
        {
            var result = action(peer);
            status = result.Status;
            return status == 200
                ? (object)new SuiteUserCommonResponse { updatedResources = queries.Project(user.BuildRefresh()) }
                : new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = result.ErrorCode, ErrorMessage = "" };
        });
        Response.StatusCode = status;
        return Encoded(bytes);
    }
}
