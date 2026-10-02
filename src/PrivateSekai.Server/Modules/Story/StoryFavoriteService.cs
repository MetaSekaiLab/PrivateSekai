extern alias game;

using System;
using System.Linq;
using game::Sekai;
using game::Sekai.StoryFavorite;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Story;

public sealed class StoryFavoriteService(UserSession user, StoryMasterQueries master)
{
    public void Set(int shareNo, string type, int storyId)
    {
        ValidateSlot(shareNo);
        if (!master.HasFavoriteStory(type, storyId))
            throw new ArgumentException("Unknown favorite story.");
        var favorites = user.Data.userStoryFavorites ?? [];
        var existing = favorites.FirstOrDefault(f => f.storyType == type && f.storyId == storyId);
        var entry = new UserStoryFavorite
        {
            shareNo = shareNo, storyType = type, storyId = storyId,
            comment = existing?.comment, isSpoiler = existing?.isSpoiler ?? false
        };
        user.Data.userStoryFavorites = favorites
            .Where(f => f.shareNo != shareNo && (f.storyType != type || f.storyId != storyId))
            .Append(entry).ToArray();
        user.MarkChanged(nameof(SuiteUser.userStoryFavorites));
    }

    public void Delete(int shareNo)
    {
        ValidateSlot(shareNo);
        user.Data.userStoryFavorites = (user.Data.userStoryFavorites ?? []).Where(f => f.shareNo != shareNo).ToArray();
        user.MarkChanged(nameof(SuiteUser.userStoryFavorites));
    }

    private void ValidateSlot(int shareNo)
    {
        var limit = master.GetConfigInt("story_favorite_count_limit");
        if (limit <= 0)
            throw new InvalidOperationException("Missing story favorite limit.");
        if (shareNo < 1 || shareNo > limit)
            throw new ArgumentException("Invalid story favorite slot.");
    }
}
