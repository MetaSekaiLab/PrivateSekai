using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Models;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

public class StoryController : PrskController
{
    private readonly UserManager _users;
    private static readonly HashSet<string> StoryRefreshDeleteTypes =
        new() { "userBeginnerMissionBehavior" };

    public StoryController(UserManager users)
    {
        _users = users;
    }

    /// <summary>
    /// 提交一个故事 episode 已读。客户端把故事类型和 episode ID 放入 path，不发送请求体；成功后合并返回的用户资源差异，并读取可能获得的故事奖励资源。
    /// </summary>
    [HttpPost("api/user/{userId}/story/{storyType}/episode/{episodeId}")]
    public IActionResult HandleStoryEpisode(long userId, string storyType, int episodeId)
    {
        var user = _users.GetUser(userId);
        var obtainedResources = user.CompleteStoryEpisode(storyType, episodeId);

        return Ok(new UserStoryResponse
        {
            updatedResources = user.GetRefreshData(deleteRtypes: StoryRefreshDeleteTypes),
            obtainedResources = obtainedResources
        });
    }

    /// <summary>
    /// 提交故事 episode 解锁消耗。客户端主要在卡牌 side story 的锁定 episode 解锁流程中请求，用指定消耗类型解锁后续故事；成功后合并资源差异并继续读故事确认流程。
    /// </summary>
    [HttpPost("api/user/{userId}/story/{storyType}/episode/{episodeId}/cost")]
    public IActionResult HandleStoryEpisodeCost(
        long userId,
        string storyType,
        int episodeId,
        [FromBody] UserStoryRequest request)
    {
        var user = _users.GetUser(userId);
        var consumedResources = user.ReleaseStoryEpisode(
            storyType,
            episodeId,
            request.cardEpisodeReleaseCostType);

        return Ok(new UserStoryCostResponse
        {
            updatedResources = user.GetRefreshData(deleteRtypes: StoryRefreshDeleteTypes),
            consumedResources = consumedResources
        });
    }

    /// <summary>
    /// 提交故事播放日志。客户端在 story 播放结束后把本次播放行为信息提交给服务端，包括是否跳过、是否自动播放、页数和连续播放状态；成功后合并用户资源并处理可能获得的资源结果。
    /// </summary>
    [HttpPost("api/user/{userId}/story/{storyType}/episode/{episodeId}/log")]
    public IActionResult HandleStoryEpisodeLog(
        long userId,
        string storyType,
        int episodeId,
        [FromBody] UserStoryLogRequest request)
    {
        var user = _users.GetUser(userId);
        user.ReadStoryEpisode(storyType, episodeId, request.noSkip);

        return Ok(new UserStoryLogResponse
        {
            updatedResources = user.GetRefreshData(deleteRtypes: StoryRefreshDeleteTypes),
            userObtainResourceResults = []
        });
    }

    /// <summary>
    /// 获取故事推荐列表。客户端用它在故事分类/推荐入口展示可继续阅读、主线、推荐或收藏相关的故事卡片。
    /// </summary>
    [HttpGet("api/user/{userId}/story/recommend")]
    public IActionResult HandleStoryRecommend(long userId)
    {
        return Ok(new UserStoryRecommendResponse
        {
            userStoryRecommends =
            [
                new UserStoryRecommend
                {
                    storyType = "unit_story",
                    storyId = 10,
                    reason = "continuously",
                    category = "continuously",
                    seq = 1
                },
                new UserStoryRecommend
                {
                    storyType = "unit_story",
                    storyId = 9,
                    reason = "main_story",
                    category = "random",
                    seq = 2
                },
                new UserStoryRecommend
                {
                    storyType = "event_story",
                    storyId = 27,
                    reason = "recommend",
                    category = "random",
                    seq = 3
                }
            ]
        });
    }

    /// <summary>
    /// 获取好友对某类故事的收藏状态。客户端用它在故事收藏/好友相关 UI 中判断哪些故事存在好友收藏状态，便于展示好友收藏提示或相关入口。
    /// </summary>
    [HttpGet("api/user/{userId}/story-favorite/friend/status/{storyType}")]
    public IActionResult HandleStoryFavoriteFriendStatus(long userId, string storyType)
    {
        return Ok(new StoryFavoriteFriendStatusResponse
        {
            friendStoryFavoriteStatuses = []
        });
    }

    /// <summary>
    /// 获取指定故事的 episode 书签列表。客户端用它恢复某个故事下已保存的 talk/episode 书签，后续新增、编辑、点击统计分别走其他书签相关接口。
    /// </summary>
    [HttpGet("api/user/{userId}/story-episode-bookmark/{storyType}/story/{storyId}")]
    public IActionResult HandleStoryEpisodeBookmark(long userId, string storyType, int storyId)
    {
        return Ok(new StoryEpisodeBookmarkResponse
        {
            userStoryEpisodeBookmarks = []
        });
    }
}
