namespace PrivateSekai.Models;

/// <summary>
/// 每用户私有数据，不随 SuiteUser 导出，字段均为推测
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
    public long? CustomMusicScoreId { get; set; }
    public long CreatedAt { get; set; }
}
