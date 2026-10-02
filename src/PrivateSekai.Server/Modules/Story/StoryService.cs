extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Story;

public sealed class StoryService(
    UserSession user,
    StoryMasterQueries master,
    MissionService missions,
    ResourceMasterQueries resourceMaster,
    ResourceService resourceService)
{
    private const string CardEpisodeReleaseCostTypeCommonMaterial = "common_material";
    private const string CardEpisodeReleaseCostTypeTicket = "card_episode_release_ticket";
    private const string CardEpisodeReleaseTicketMaterialIdConfig = "card_episode_release_ticket_material_id";
    private const string CardEpisodeReleaseCostQuantityConfig = "card_episode_release_cost_quantity";

    public bool IsUnitEpisodeRead(int episodeId) =>
        user.Data.userUnitEpisodeStatuses?.Any(s => s.episodeId == episodeId && s.status == "already_read") == true;

    public void ReadStoryEpisode(string storyType, int episodeId, bool isNotSkipped = false)
    {
        switch (storyType)
        {
            case "unit_story":
                MarkEpisodeRead(user.Data.userUnitEpisodeStatuses, episodeId,
                    nameof(SuiteUser.userUnitEpisodeStatuses), isNotSkipped);
                break;
            case "special_story":
                MarkEpisodeRead(user.Data.userSpecialEpisodeStatuses, episodeId,
                    nameof(SuiteUser.userSpecialEpisodeStatuses), isNotSkipped);
                break;
            case "character_profile_story":
                MarkEpisodeRead(user.Data.userCharacterProfileEpisodeStatuses, episodeId,
                    nameof(SuiteUser.userCharacterProfileEpisodeStatuses), isNotSkipped);
                break;
            case "event_story":
                MarkEventEpisodeRead(user.Data.userEventEpisodeStatuses, episodeId,
                    nameof(SuiteUser.userEventEpisodeStatuses), isNotSkipped);
                break;
            case "archive_event_story":
                MarkArchiveEventEpisodeRead(user.Data.userArchiveEventEpisodeStatuses, episodeId,
                    nameof(SuiteUser.userArchiveEventEpisodeStatuses), isNotSkipped);
                break;
            case "card_story":
                MarkCardEpisodeRead(episodeId, isNotSkipped);
                break;
        }
    }

    public UserResource[] CompleteStoryEpisode(string storyType, int episodeId, bool isNotSkipped = false)
    {
        if (storyType == "card_story")
            return CompleteCardEpisode(episodeId, isNotSkipped);

        if (storyType is "unit_story" or "special_story")
        {
            var statuses = storyType == "unit_story" ? user.Data.userUnitEpisodeStatuses : user.Data.userSpecialEpisodeStatuses;
            var status = statuses?.SingleOrDefault(s => s.episodeId == episodeId)
                ?? throw new ArgumentException("Story episode is not available.");
            if (status.status == "already_read")
                return [];
            if (status.status != "released" && !(storyType == "unit_story" &&
                status.status == "unreleased" && master.AreUnitEpisodeConditionsMet(episodeId, user.Data.userReleaseConditions)))
                throw new ArgumentException("Story episode is locked.");
            var rewards = master.GetEpisodeRewardBoxIds(storyType, episodeId).SelectMany(id =>
            {
                var resources = resourceMaster.BuildResourcesFromBox("episode_reward", id);
                return resources.Length > 0 ? resources : throw new InvalidOperationException("Missing episode reward box.");
            }).ToArray();
            resourceService.Grant(rewards);
            ReadStoryEpisode(storyType, episodeId, isNotSkipped);
            if (storyType == "unit_story")
            {
                AdvanceUnitStory(episodeId);
                missions.RecordUnitStoryRead(master.GetUnitEpisodeUnit(episodeId));
            }
            return rewards;
        }

        ReadStoryEpisode(storyType, episodeId, isNotSkipped);
        return [];
    }

    private void AdvanceUnitStory(int episodeId)
    {
        var conditions = (user.Data.userReleaseConditions ?? []).ToList();
        foreach (var id in master.GetUnitEpisodeClearedConditionIds(episodeId))
        {
            if (conditions.Any(c => c.releaseConditionId == id)) continue;
            conditions.Add(new UserReleaseCondition { releaseConditionId = id, createdAt = user.Now });
            user.Data.userReleaseConditions = conditions.ToArray();
            user.MarkChanged(nameof(SuiteUser.userReleaseConditions));
        }

        var statuses = user.Data.userUnitEpisodeStatuses.ToList();
        foreach (var status in statuses.Where(s => s.status == "can_not_read"))
        {
            if (!master.AreUnitEpisodeConditionsMet(status.episodeId, user.Data.userReleaseConditions)) continue;
            status.status = "unreleased";
            user.MarkChanged(nameof(SuiteUser.userUnitEpisodeStatuses));
        }

        var next = master.GetNextUnitEpisode(episodeId);
        if (next == null || statuses.Any(s => s.episodeId == next.id)) return;
        statuses.Add(new UserEpisodeStatus
        {
            storyType = "unit_story", episodeId = next.id,
            status = master.AreUnitEpisodeConditionsMet(next.id, user.Data.userReleaseConditions) ? "unreleased" : "can_not_read"
        });
        user.Data.userUnitEpisodeStatuses = statuses.ToArray();
        user.MarkChanged(nameof(SuiteUser.userUnitEpisodeStatuses));
    }

    public UserResource[] ReleaseStoryEpisode(string storyType, int episodeId, string? costType)
    {
        if (storyType == "card_story")
            return ReleaseCardEpisode(episodeId, costType);

        return [];
    }

    private void MarkEpisodeRead(UserEpisodeStatus[]? statuses, int episodeId, string fieldName, bool isNotSkipped)
    {
        if (statuses == null) return;

        foreach (var status in statuses)
        {
            if (status.episodeId != episodeId) continue;

            var changed = status.status != "already_read" || status.isNotSkipped != isNotSkipped;
            status.status = "already_read";
            status.isNotSkipped = isNotSkipped;
            if (changed)
                user.MarkChanged(fieldName);
            return;
        }
    }

    private void MarkEventEpisodeRead(UserEventEpisodeStatus[]? statuses, int episodeId, string fieldName, bool isNotSkipped)
    {
        if (statuses == null) return;

        foreach (var status in statuses)
        {
            if (status.episodeId != episodeId) continue;

            var changed = status.status != "already_read" || status.isNotSkipped != isNotSkipped;
            status.status = "already_read";
            status.isNotSkipped = isNotSkipped;
            if (changed)
                user.MarkChanged(fieldName);
            return;
        }
    }

    private void MarkArchiveEventEpisodeRead(UserArchiveEventEpisodeStatus[]? statuses, int episodeId, string fieldName, bool isNotSkipped)
    {
        if (statuses == null) return;

        foreach (var status in statuses)
        {
            if (status.episodeId != episodeId) continue;

            var changed = status.status != "already_read" || status.isNotSkipped != isNotSkipped;
            status.status = "already_read";
            status.isNotSkipped = isNotSkipped;
            if (changed)
                user.MarkChanged(fieldName);
            return;
        }
    }

    private UserCardEpisode? FindCardEpisode(int cardEpisodeId)
    {
        if (user.Data.userCards == null) return null;

        foreach (var card in user.Data.userCards)
        {
            if (card.episodes == null) continue;

            foreach (var episode in card.episodes)
            {
                if (episode.cardEpisodeId == cardEpisodeId)
                    return episode;
            }
        }

        return null;
    }

    private void MarkCardEpisodeRead(int cardEpisodeId, bool isNotSkipped)
    {
        var episode = FindCardEpisode(cardEpisodeId);
        if (episode == null) return;

        var changed = episode.scenarioStatus != "already_read" || episode.isNotSkipped != isNotSkipped;
        episode.scenarioStatus = "already_read";
        episode.scenarioStatusReasons = [];
        episode.isNotSkipped = isNotSkipped;
        if (changed)
            user.MarkChanged(nameof(SuiteUser.userCards));
    }

    private UserResource[] CompleteCardEpisode(int cardEpisodeId, bool isNotSkipped)
    {
        var episode = FindCardEpisode(cardEpisodeId);
        var wasAlreadyRead = string.Equals(episode?.scenarioStatus, "already_read", StringComparison.Ordinal);

        MarkCardEpisodeRead(cardEpisodeId, isNotSkipped);

        if (episode == null || wasAlreadyRead)
            return [];

        var rewards = resourceMaster.BuildResourcesFromBoxes(
            "episode_reward",
            master.GetMasterCardEpisode(cardEpisodeId)?.rewardResourceBoxIds);

        // 故事奖励仅处理这些资源类型。
        resourceService.Grant(rewards.Where(r => r.resourceType is
            "jewel" or "coin" or "virtual_coin" or "material" or "practice_ticket" or "costume_3d"));

        user.MarkChanged(new[]
        {
            nameof(SuiteUser.userCharacterMissions),
            nameof(SuiteUser.userCharacterMissionStatuses)
        });

        return rewards;
    }

    private UserResource[] ReleaseCardEpisode(int cardEpisodeId, string? costType)
    {
        var episode = FindCardEpisode(cardEpisodeId);
        if (episode == null)
            return [];

        var wasUnlocked =
            string.Equals(episode.scenarioStatus, "released", StringComparison.Ordinal) ||
            string.Equals(episode.scenarioStatus, "already_read", StringComparison.Ordinal);

        if (wasUnlocked)
            return [];

        var consumed = BuildCardEpisodeReleaseCosts(cardEpisodeId, costType);
        foreach (var resource in consumed)
        {
            resourceService.Consume(resource.resourceType, resource.resourceId, resource.quantity);
        }

        episode.scenarioStatus = "released";
        episode.scenarioStatusReasons = [];
        user.MarkChanged(nameof(SuiteUser.userCards));

        return consumed;
    }

    private UserResource[] BuildCardEpisodeReleaseCosts(int cardEpisodeId, string? costType)
    {
        var normalizedCostType = string.IsNullOrEmpty(costType)
            ? CardEpisodeReleaseCostTypeCommonMaterial
            : costType;

        if (string.Equals(normalizedCostType, CardEpisodeReleaseCostTypeTicket, StringComparison.Ordinal))
        {
            var materialId = master.GetConfigInt(CardEpisodeReleaseTicketMaterialIdConfig);
            var quantity = master.GetConfigInt(CardEpisodeReleaseCostQuantityConfig, 1);
            return materialId <= 0 || quantity <= 0
                ? []
                :
                [
                    new UserResource
                    {
                        resourceType = "material",
                        resourceId = materialId,
                        quantity = quantity
                    }
                ];
        }

        var costs = master.GetMasterCardEpisode(cardEpisodeId)?.costs;
        if (costs == null)
            return [];

        return costs
            .Where(cost => cost.quantity > 0)
            .Select(cost => new UserResource
            {
                resourceType = cost.resourceType,
                resourceId = cost.resourceId,
                quantity = cost.quantity
            })
            .ToArray();
    }
}
