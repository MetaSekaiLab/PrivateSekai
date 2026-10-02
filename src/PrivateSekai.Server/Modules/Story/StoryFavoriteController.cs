extern alias game;

using game::Sekai;
using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Story;

public sealed class StoryFavoriteController(UserOperation operations, UserSession user, StoryFavoriteService favorites) : PrskController
{
    [HttpPost("api/user/{userId}/story-favorite/{shareNo:int}/{storyType}/{storyId}")]
    public IActionResult Set(long userId, int shareNo, string storyType, int storyId) =>
        Encoded(operations.Execute(userId, () =>
        {
            favorites.Set(shareNo, storyType, storyId);
            return new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() };
        }));

    [HttpDelete("api/user/{userId}/story-favorite/{shareNo:int}")]
    public IActionResult Delete(long userId, int shareNo) =>
        Encoded(operations.Execute(userId, () =>
        {
            favorites.Delete(shareNo);
            return new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() };
        }));
}
