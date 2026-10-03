extern alias game;

using System.Collections.Generic;
using game::Sekai;
namespace PrivateSekai.Models;

/// <summary>
/// 每用户私有存储，不随 SuiteUser 导出。
/// </summary>
public class NotSuiteData
{
    /// <summary>
    /// 引继码 ID
    /// </summary>
    public string InheritId { get; set; } = "";
    /// <summary>
    /// 引继码密码
    /// </summary>
    public string InheritPassword { get; set; } = "";

    /// <summary>礼物领取历史</summary>
    public List<UserPresentHistoryData> PresentHistories { get; set; } = [];

    /// <summary>进行中的单人 live session</summary>
    public Dictionary<string, UserLiveSessionData> UserLiveSessions { get; set; } = [];

    public Dictionary<string, UserChallengeLiveStartRequest> ChallengeLiveSessions { get; set; } = [];

    public Dictionary<string, List<StoryBookmarkData>> StoryBookmarks { get; set; } = [];
}

public sealed class StoryBookmarkData
{
    public UserStoryEpisodeBookmark Bookmark { get; set; } = new();
    public byte[] Thumbnail { get; set; } = [];
    public string NameEditStatus { get; set; } = "";
    public long ClickCount { get; set; }
}

public class UserLiveSessionData
{
    public string UserLiveId { get; set; } = "";
    public int MusicId { get; set; }
    public int MusicDifficultyId { get; set; }
    public int MusicVocalId { get; set; }
    public int DeckId { get; set; }
    public int BoostCount { get; set; }
    public bool IsAuto { get; set; }
    public string? MusicCategoryName { get; set; }
    public string? CustomMusicScoreId { get; set; }
    public long CreatedAt { get; set; }
}
