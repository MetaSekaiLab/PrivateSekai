extern alias game;

using System.Linq;
using game::Sekai;
using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;
using EmptyResponse = PrivateSekai.Models.EmptyResponse;

namespace PrivateSekai.Modules.Story;

public sealed class StoryBookmarkController(UserOperation operations, UserSession user, StoryBookmarkService bookmarks) : PrskController
{
    private const string StoryPath = "api/user/{userId}/story-episode-bookmark/{storyType}/story/{storyId}";
    private const string BookmarkPath = StoryPath + "/episode/{episodeId}/talk/{talkId}";

    [HttpGet(StoryPath)]
    public IActionResult Get(long userId, string storyType, int storyId) =>
        Encoded(operations.Query(userId, () => new StoryEpisodeBookmarkResponse
        {
            userStoryEpisodeBookmarks = bookmarks.Get(storyType, storyId), updatedResources = user.BuildRefresh()
        }));

    [HttpPost(BookmarkPath)]
    public IActionResult Add(long userId, string storyType, int storyId, int episodeId, int talkId,
        [FromBody] PostStoryEpisodeBookmarkRequest request) =>
        Encoded(operations.Execute(userId, () => new PostStoryEpisodeBookmarkResponse
        {
            userStoryEpisodeBookmark = bookmarks.Add(storyType, storyId, episodeId, talkId, request),
            updatedResources = user.BuildRefresh()
        }));

    [HttpPatch(BookmarkPath)]
    public IActionResult Rename(long userId, string storyType, int storyId, int episodeId, int talkId,
        [FromBody] PatchStoryEpisodeBookmarkRequest request) =>
        Encoded(operations.Execute(userId, () => bookmarks.Rename(storyType, storyId, episodeId, talkId, request)));

    [HttpDelete(BookmarkPath)]
    public IActionResult Delete(long userId, string storyType, int storyId, int episodeId, int talkId) =>
        Encoded(operations.Execute(userId, () =>
        {
            bookmarks.Delete(storyType, storyId, episodeId, talkId);
            return new StoryEpisodeBookmarkResponse
            {
                userStoryEpisodeBookmarks = bookmarks.Get(storyType, storyId), updatedResources = user.BuildRefresh()
            };
        }));

    [HttpPost(BookmarkPath + "/click")]
    public IActionResult Click(long userId, string storyType, int storyId, int episodeId, int talkId) =>
        Encoded(operations.Execute(userId, () =>
        {
            bookmarks.RecordClick(storyType, storyId, episodeId, talkId);
            return new EmptyResponse();
        }));

    [PrskPlaintextResponse]
    [HttpGet("image/bookmark-story/thumbnail/{userId}/{hash}")]
    public IActionResult Thumbnail(long userId, string hash)
    {
        var path = $"image/bookmark-story/thumbnail/{userId}/{hash}";
        var entry = operations.Read(userId)?.Private.StoryBookmarks.Values.SelectMany(b => b)
            .FirstOrDefault(b => b.Bookmark.thumbnailPath == path);
        return entry == null ? NotFound() : File(entry.Thumbnail, "image/jpeg");
    }
}
