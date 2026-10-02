extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Story;

public sealed class StoryMasterQueries(MasterData master)
{
    public bool IsUnconditionalUnitEpisode(int episodeId)
    {
        var episode = master.GetTable<MasterUnitStory>("unitStories").Rows
            .SelectMany(s => s.chapters ?? []).SelectMany(c => c.episodes ?? [])
            .SingleOrDefault(e => e.id == episodeId);
        if (episode == null) return false;
        var conditions = master.GetTable<MasterReleaseCondition>("releaseConditions", c => c.id);
        return conditions.FindById(episode.releaseConditionId)?.releaseConditionType == "none" &&
            (episode.andReleaseConditionId == 0 ||
             conditions.FindById(episode.andReleaseConditionId)?.releaseConditionType == "none");
    }

    public int[] GetEpisodeRewardBoxIds(string type, int episodeId) => type switch
    {
        "unit_story" => master.GetTable<MasterUnitStory>("unitStories").Rows
            .SelectMany(s => s.chapters ?? []).SelectMany(c => c.episodes ?? [])
            .SingleOrDefault(e => e.id == episodeId)?.rewardResourceBoxIds
            ?? throw new ArgumentException("Unknown unit story episode or missing rewards."),
        "special_story" => master.GetTable<MasterSpecialStory>("specialStories").Rows
            .SelectMany(s => s.episodes ?? []).SingleOrDefault(e => e.id == episodeId)?.rewardResourceBoxIds
            ?? throw new ArgumentException("Unknown special story episode or missing rewards."),
        _ => throw new ArgumentException("Unsupported story reward type.")
    };

    public bool HasFavoriteStory(string type, int id) => type switch
    {
        "unit_story" => master.GetTable<MasterUnitStoryEpisodeGroup>("unitStoryEpisodeGroups", s => s.id).FindById(id) != null,
        "event_story" => master.GetTable<MasterEventStory>("eventStories", s => s.id).FindById(id) != null,
        _ => false
    };

    public MasterCardEpisode? GetMasterCardEpisode(int cardEpisodeId) =>
        master.GetTable<MasterCardEpisode>("cardEpisodes", e => e.id).FindById(cardEpisodeId);

    public int GetConfigInt(string configKey, int fallback = 0)
    {
        var config = master.GetTable<MasterConfig>("configs").Rows
            .FirstOrDefault(c => string.Equals(c.configKey, configKey, StringComparison.Ordinal));

        return int.TryParse(config?.value, out var value) ? value : fallback;
    }
}
