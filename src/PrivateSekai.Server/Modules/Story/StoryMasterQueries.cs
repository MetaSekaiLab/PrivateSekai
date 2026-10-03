extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Story;

public sealed class StoryMasterQueries(MasterData master)
{
    public int[] GetCardEpisodePairIds(int episodeId)
    {
        var episode = GetMasterCardEpisode(episodeId);
        if (episode == null) return [];
        var parts = master.GetTable<MasterCardEpisode>("cardEpisodes", e => e.id).Rows
            .Where(e => e.cardId == episode.cardId).ToArray();
        var first = parts.SingleOrDefault(e => e.cardEpisodePartType == "first_part");
        var second = parts.SingleOrDefault(e => e.cardEpisodePartType == "second_part");
        return first == null || second == null ? [] : [first.id, second.id];
    }

    public int[] GetFollowingCardEpisodeIds(int episodeId)
    {
        var episode = GetMasterCardEpisode(episodeId);
        return episode?.cardEpisodePartType != "first_part" ? [] :
            master.GetTable<MasterCardEpisode>("cardEpisodes", e => e.id).Rows
                .Where(e => e.cardId == episode.cardId && e.cardEpisodePartType == "second_part")
                .Select(e => e.id).ToArray();
    }

    public bool IsCardEpisodeLevelMet(int episodeId, int cardId, int level)
    {
        var episode = GetMasterCardEpisode(episodeId);
        if (episode == null || episode.cardId != cardId) return false;
        var condition = master.GetTable<MasterReleaseCondition>("releaseConditions", c => c.id)
            .FindById(episode.releaseConditionId);
        return condition?.releaseConditionType == "card_level" && condition.releaseConditionTypeId == cardId &&
            level >= condition.releaseConditionTypeLevel;
    }

    public string? GetUnitEpisodeUnit(int episodeId) =>
        master.GetTable<MasterUnitStory>("unitStories").Rows
            .SelectMany(s => s.chapters ?? [])
            .SingleOrDefault(c => c.episodes?.Any(e => e.id == episodeId) == true)?.unit;

    public int[] GetUnitEpisodeClearedConditionIds(int episodeId) =>
        master.GetTable<MasterReleaseCondition>("releaseConditions", c => c.id).Rows
            .Where(c => c.releaseConditionType == "unit_story" && c.releaseConditionTypeId == episodeId)
            .Select(c => c.id).ToArray();

    public MasterUnitStoryEpisode? GetNextUnitEpisode(int episodeId)
    {
        var chapter = master.GetTable<MasterUnitStory>("unitStories").Rows
            .SelectMany(s => s.chapters ?? [])
            .SingleOrDefault(c => c.episodes?.Any(e => e.id == episodeId) == true);
        var current = chapter?.episodes.Single(e => e.id == episodeId);
        return current == null ? null : chapter!.episodes
            .Where(e => e.unitStoryEpisodeGroupId == current.unitStoryEpisodeGroupId && e.episodeNo > current.episodeNo)
            .OrderBy(e => e.episodeNo).FirstOrDefault();
    }

    public bool AreUnitEpisodeConditionsMet(int episodeId, UserReleaseCondition[]? cleared)
    {
        var episode = master.GetTable<MasterUnitStory>("unitStories").Rows
            .SelectMany(s => s.chapters ?? []).SelectMany(c => c.episodes ?? [])
            .SingleOrDefault(e => e.id == episodeId);
        if (episode == null) return false;
        var conditions = master.GetTable<MasterReleaseCondition>("releaseConditions", c => c.id);
        bool IsClear(int id) => cleared?.Any(c => c.releaseConditionId == id) == true ||
            conditions.FindById(id)?.releaseConditionType == "none";
        return IsClear(episode.releaseConditionId) &&
            (episode.andReleaseConditionId == 0 || IsClear(episode.andReleaseConditionId));
    }

    public bool AreSpecialEpisodeConditionsMet(int episodeId, UserReleaseCondition[]? cleared)
    {
        var episode = master.GetTable<MasterSpecialStory>("specialStories").Rows
            .SelectMany(s => s.episodes ?? []).SingleOrDefault(e => e.id == episodeId);
        if (episode == null) return false;
        return cleared?.Any(c => c.releaseConditionId == episode.releaseConditionId) == true ||
            master.GetTable<MasterReleaseCondition>("releaseConditions", c => c.id)
                .FindById(episode.releaseConditionId)?.releaseConditionType == "none";
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
