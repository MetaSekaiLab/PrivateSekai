extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using game::Sekai;
using PrivateSekai.Models;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Story;

public sealed class StoryBookmarkService(UserSession user, StoryMasterQueries master)
{
    public UserStoryEpisodeBookmark[] Get(string type, int storyId)
    {
        ValidateStory(type, storyId);
        return Entries(type).Where(b => b.Bookmark.storyId == storyId).Select(b => b.Bookmark).ToArray();
    }

    public UserStoryEpisodeBookmark Add(string type, int storyId, int episodeId, int talkId,
        PostStoryEpisodeBookmarkRequest request)
    {
        ValidateStory(type, storyId);
        ValidateName(request.name, request.bookmarkNameEditStatus);
        if (episodeId <= 0 || talkId < 0)
            throw new ArgumentException("Invalid bookmark position.");
        var entries = Entries(type);
        if (entries.Any(b => Matches(b, storyId, episodeId, talkId)))
            throw new ArgumentException("Bookmark already exists.");
        if (entries.Count(b => b.Bookmark.storyId == storyId) >= RequiredConfig("story_episode_bookmark_count"))
            throw new ArgumentException("Bookmark limit reached.");
        var bytes = Convert.FromBase64String(request.thumbnail ?? "");
        if (bytes.Length < 3 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[2] != 0xff)
            throw new ArgumentException("Bookmark thumbnail must be JPEG.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var bookmark = new UserStoryEpisodeBookmark
        {
            storyId = storyId, storyEpisodeId = episodeId, talkId = talkId, name = request.name,
            thumbnailPath = $"image/bookmark-story/thumbnail/{user.UserId}/{hash}", createdAt = user.Now
        };
        entries.Add(new StoryBookmarkData { Bookmark = bookmark, Thumbnail = bytes, NameEditStatus = request.bookmarkNameEditStatus });
        user.Private.StoryBookmarks[type] = entries;
        RefreshStories();
        return bookmark;
    }

    public UserStoryEpisodeBookmark Rename(string type, int storyId, int episodeId, int talkId,
        PatchStoryEpisodeBookmarkRequest request)
    {
        ValidateName(request.name, request.bookmarkNameEditStatus);
        var entry = Find(type, storyId, episodeId, talkId);
        entry.Bookmark.name = request.name;
        entry.NameEditStatus = request.bookmarkNameEditStatus;
        return entry.Bookmark;
    }

    public void Delete(string type, int storyId, int episodeId, int talkId)
    {
        var entry = Find(type, storyId, episodeId, talkId);
        user.Private.StoryBookmarks[type].Remove(entry);
        RefreshStories();
    }

    public void RecordClick(string type, int storyId, int episodeId, int talkId)
    {
        var entry = Find(type, storyId, episodeId, talkId);
        entry.ClickCount = checked(entry.ClickCount + 1);
    }

    private StoryBookmarkData Find(string type, int storyId, int episodeId, int talkId)
    {
        ValidateStory(type, storyId);
        return Entries(type).SingleOrDefault(b => Matches(b, storyId, episodeId, talkId))
            ?? throw new ArgumentException("Bookmark does not exist.");
    }

    private List<StoryBookmarkData> Entries(string type) =>
        user.Private.StoryBookmarks.GetValueOrDefault(type) ?? [];

    private static bool Matches(StoryBookmarkData entry, int storyId, int episodeId, int talkId) =>
        entry.Bookmark.storyId == storyId && entry.Bookmark.storyEpisodeId == episodeId && entry.Bookmark.talkId == talkId;

    private static void ValidateStory(string type, int storyId)
    {
        if (storyId <= 0 || !Enum.GetNames<StoryType>().Contains(type, StringComparer.Ordinal))
            throw new ArgumentException("Invalid story reference.");
    }

    private void ValidateName(string? name, string? status)
    {
        if (name == null || name.Length > RequiredConfig("story_episode_bookmark_name_max_length") || status is not ("none" or "edit"))
            throw new ArgumentException("Invalid bookmark name.");
    }

    private int RequiredConfig(string key)
    {
        var value = master.GetConfigInt(key);
        return value > 0 ? value : throw new InvalidOperationException($"Missing or invalid config '{key}'.");
    }

    private void RefreshStories()
    {
        user.Data.userBookmarkedStories = user.Private.StoryBookmarks
            .SelectMany(group => group.Value.Select(b => b.Bookmark.storyId).Distinct()
                .Select(id => new UserBookmarkedStory { storyType = group.Key, storyId = id })).ToArray();
        user.MarkChanged(nameof(SuiteUser.userBookmarkedStories));
    }
}
