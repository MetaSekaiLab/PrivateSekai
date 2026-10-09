extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Characters;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Missions;

public sealed class MissionService(
    UserSession user,
    MissionMasterQueries master,
    ResourceMasterQueries resourceMaster,
    ResourceService resourceService,
    CharacterService characters)
{
    private const string BeginnerMissionV2Type = "beginner_mission_v2";

    public UserCharacterMissionV2Status[] ReceiveCharacterMissions(int characterId, string? type)
    {
        if (type is not (null or "COLLECT_COSTUME_3D" or "COLLECT_ANOTHER_VOCAL" or "COLLECT_CHARACTER_ARCHIVE_VOICE" or "COLLECT_MEMBER" or
            "READ_CARD_EPISODE_FIRST" or "READ_CARD_EPISODE_SECOND" or "AREA_ITEM_LEVEL_UP_CHARACTER" or "PLAY_LIVE" or "WAITING_ROOM"))
            throw new NotSupportedException("Character mission type is not verified.");
        var definitions = master.GetCharacterMissions(characterId).ToDictionary(m => m.id);
        var statuses = (user.Data.userCharacterMissionStatuses ?? []).Where(s => s.characterId == characterId &&
            s.missionStatus == "achieved" && (type == null ||
                (definitions.TryGetValue(s.missionId, out var definition) && definition.characterMissionType.ToUpperInvariant() == type)))
            .OrderBy(s => s.missionId).ThenBy(s => s.parameterGroupId).ThenBy(s => s.seq).ToArray();
        if (statuses.Length == 0)
        {
            if (user.Data.userCharacters?.Any(c => c.characterId == characterId) != true)
                throw new ArgumentException("Unknown character.");
            user.MarkChanged(nameof(SuiteUser.userCharacters));
            return [];
        }
        var experience = 0;
        foreach (var status in statuses)
        {
            if (!definitions.TryGetValue(status.missionId, out var definition))
                throw new InvalidOperationException("Missing character mission definition.");
            if (definition.characterMissionType is not ("collect_costume_3d" or "collect_another_vocal" or "collect_character_archive_voice" or "collect_member" or
                "read_card_episode_first" or "read_card_episode_second" or "area_item_level_up_character" or "play_live" or "waiting_room"))
                throw new NotSupportedException("Character mission type is not verified.");
            var parameter = master.GetCharacterMissionParameters(definition.parameterGroupId)
                .SingleOrDefault(p => p.id == status.parameterGroupId && p.seq == status.seq)
                ?? throw new InvalidOperationException("Missing character mission parameter.");
            if (parameter.exp <= 0 || parameter.quantity != 0)
                throw new NotSupportedException("Character mission reward is not verified.");
            experience = checked(experience + parameter.exp);
        }
        characters.Gain(characterId, experience);
        foreach (var status in statuses) status.missionStatus = "received";
        user.MarkChanged(nameof(SuiteUser.userCharacterMissionStatuses));
        return statuses;
    }

    public void RecordInitialLogin()
    {
        var records = (user.Data.userHonorMissions ?? []).ToList();
        foreach (var type in new[] { "login_continued", "login_total" })
        {
            if (records.Any(m => m.honorMissionType == type))
                throw new NotSupportedException("Existing login honor progress is not verified.");
            records.Add(new UserHonorMission { honorMissionType = type, progress = 1, achievedMissionIds = [] });
        }
        user.Data.userHonorMissions = records.OrderBy(m => m.honorMissionType, StringComparer.Ordinal).ToArray();
        user.MarkChanged(nameof(SuiteUser.userHonorMissions));
    }

    public bool RecordEventItemConsumption(int eventId, int quantity)
    {
        if (quantity <= 0) throw new ArgumentException("Invalid event item consumption.");
        var definitions = master.GetEventItemConsumptionMissions(eventId);
        var records = (user.Data.userEventMissions ?? []).ToList();
        // 达成时的状态和奖励联动待核验，先检查整组，避免写入部分进度。
        if (definitions.Any(d => d.eventMissionCategory != "normal" || d.requirement2 != 0 ||
            checked((records.SingleOrDefault(m => m.eventId == eventId && m.eventMissionId == d.id)?.progress ?? 0) + quantity) >= d.requirement1))
            return false;
        foreach (var definition in definitions)
        {
            var record = records.SingleOrDefault(m => m.eventId == eventId && m.eventMissionId == definition.id);
            if (record == null)
            {
                record = new UserEventMission { eventId = eventId, eventMissionId = definition.id };
                records.Add(record);
            }
            record.progress += quantity;
            record.isNewAchieved = false;
        }
        if (definitions.Length > 0)
        {
            user.Data.userEventMissions = records.ToArray();
            user.MarkChanged(nameof(SuiteUser.userEventMissions));
        }
        return true;
    }

    public UserCharacterMissionV2Status[] RecordAreaItemUpgrade(int areaItemId) =>
        RecordCharacterMissionProgress(master.GetAreaItemCharacterMissions(areaItemId));

    public void RecordCardEpisodeRead(int episodeId) =>
        RecordCharacterMissionProgress(master.GetCardEpisodeMissions(episodeId));

    public void RecordCardSpecialTraining(int characterId) =>
        RecordCharacterMissionProgress(master.GetCardCollectionMissions(characterId));

    private UserCharacterMissionV2Status[] RecordCharacterMissionProgress(IEnumerable<MasterCharacterMissionV2> definitions)
    {
        var achieved = new List<UserCharacterMissionV2Status>();
        foreach (var definition in definitions)
        {
            var missions = (user.Data.userCharacterMissions ?? []).ToList();
            var progress = missions.SingleOrDefault(m => m.characterId == definition.characterId &&
                m.characterMissionType == definition.characterMissionType);
            if (progress == null)
            {
                progress = new UserCharacterMissionV2
                {
                    characterId = definition.characterId, characterMissionType = definition.characterMissionType,
                    achievedMissions = []
                };
                missions.Add(progress);
            }
            progress.progress++;
            var statuses = (user.Data.userCharacterMissionStatuses ?? []).ToList();
            foreach (var parameter in master.GetCharacterMissionParameters(definition.parameterGroupId))
            {
                if (progress.progress < parameter.requirement || statuses.Any(s => s.missionId == definition.id &&
                    s.parameterGroupId == parameter.id && s.seq == parameter.seq && s.characterId == definition.characterId)) continue;
                var status = new UserCharacterMissionV2Status
                {
                    userId = user.UserId, missionId = definition.id, parameterGroupId = parameter.id,
                    seq = parameter.seq, characterId = definition.characterId, missionStatus = "achieved"
                };
                statuses.Add(status);
                achieved.Add(status);
            }
            user.Data.userCharacterMissions = missions.ToArray();
            user.MarkChanged(nameof(SuiteUser.userCharacterMissions));
            if (statuses.Count != (user.Data.userCharacterMissionStatuses?.Length ?? 0))
            {
                user.Data.userCharacterMissionStatuses = statuses.OrderBy(s => s.missionId)
                    .ThenBy(s => s.parameterGroupId).ThenBy(s => s.seq).ToArray();
                user.MarkChanged(nameof(SuiteUser.userCharacterMissionStatuses));
            }
        }
        return achieved.ToArray();
    }

    public void RecordAreaItemPurchase() => RecordBeginnerMissionProgress(master.GetAreaItemPurchaseMissions());

    public void RecordCardStoryRead() => RecordBeginnerMissionProgress(master.GetCardStoryMissions());

    public void RecordUnitStoryRead(string? unit) => RecordBeginnerMissionProgress(master.GetUnitStoryMissions(unit));

    public void RecordCardPracticeLevelUp(int levels) => RecordBeginnerMissionProgress(master.GetCardLevelMissions(), levels);

    public void RecordManualLiveClear() => RecordLimitedBeginnerProgress(master.GetLiveClearMissions());

    public void RecordEasyFullCombo() => UpdateHonorProgress("easy_full_combo");

    public void RecordNormalFullCombo() => UpdateHonorProgress("normal_full_combo");

    public void RecordLiveFinish(bool cleared) =>
        UpdateHonorProgress(cleared ? "clear_live" : "finish_live_with_empty_life");

    public void RecordLiveRecords(int combo, int playLevel)
    {
        UpdateHonorProgress("clear_live_combo", combo);
        UpdateHonorProgress("play_level_clear", playLevel);
    }

    private void UpdateHonorProgress(string type, int? maximum = null)
    {
        var records = (user.Data.userHonorMissions ?? []).ToList();
        var progress = records.SingleOrDefault(m => m.honorMissionType == type);
        if (progress == null)
        {
            progress = new UserHonorMission { honorMissionType = type, achievedMissionIds = [] };
            var index = records.FindIndex(m => string.CompareOrdinal(m.honorMissionType, type) > 0);
            records.Insert(index < 0 ? records.Count : index, progress);
        }
        progress.progress = maximum.HasValue ? Math.Max(progress.progress, maximum.Value) : checked(progress.progress + 1);
        user.Data.userHonorMissions = records.ToArray();
        user.MarkChanged(nameof(SuiteUser.userHonorMissions));
        foreach (var definition in master.GetHonorMissions(type))
        {
            if (progress.progress < definition.requirement ||
                user.Data.userMissionStatuses?.Any(s => s.missionType == "honor_mission" && s.missionId == definition.id &&
                    s.missionStatus is "achieved" or "received") == true) continue;
            MarkMissionAchieved("honor_mission", definition.id);
            user.MarkChanged(nameof(SuiteUser.userMissionStatuses));
        }
    }

    public void RecordChallengeLiveClear() => RecordBeginnerMissionProgress(master.GetChallengeLiveClearMissions());

    public void RecordCharacterLiveClear(int characterId)
    {
        RecordCharacterMissionProgress(master.GetCharacterLiveMissions(characterId));
        user.Data.userCharacterMissions = (user.Data.userCharacterMissions ?? [])
            .OrderBy(m => m.characterMissionType, StringComparer.Ordinal).ThenBy(m => m.characterId).ToArray();
    }

    public void RecordMusicPurchase() => RecordLimitedBeginnerProgress(master.GetMusicPurchaseMissions());

    public void RecordMusicVideoWatch() => RecordBeginnerMissionProgress(master.GetMusicVideoMissions());

    public void RecordCostumeChange() => RecordBeginnerMissionProgress(master.GetCostumeChangeMissions());

    public UserCharacterMissionV2Status[] RecordStampPurchase(int characterId)
    {
        UpdateHonorProgress("collect_stamp");
        return RecordCharacterMissionProgress(master.GetStampCollectionMissions(characterId));
    }

    public UserCharacterMissionV2Status[] RecordCostumeCraft(int characterId)
    {
        UpdateHonorProgress("collect_costume_3d");
        RecordBeginnerMissionProgress(master.GetCostumeCraftMissions());
        return RecordCharacterMissionProgress(master.GetCostumeCollectionMissions(characterId));
    }

    public UserCharacterMissionV2Status[] RecordAnotherVocalPurchase(int[] characterIds)
    {
        UpdateHonorProgress("collect_another_vocal");
        return RecordCharacterMissionProgress(characterIds.Distinct().SelectMany(master.GetAnotherVocalCollectionMissions));
    }

    private void RecordLimitedBeginnerProgress(IEnumerable<MasterBeginnerMissionV2> definitions)
    {
        foreach (var definition in definitions)
        {
            var records = (user.Data.userBeginnerMissionV2s ?? []).ToList();
            var progress = records.SingleOrDefault(m => m.beginnerMissionV2Id == definition.id);
            if (progress?.progress >= definition.requirement ||
                user.Data.userMissionStatuses?.Any(s => s.missionType == BeginnerMissionV2Type &&
                    s.missionId == definition.id && s.missionStatus is "achieved" or "received") == true)
                continue;
            if (progress == null)
            {
                progress = new UserBeginnerMissionV2 { beginnerMissionV2Id = definition.id };
                records.Add(progress);
            }
            progress.progress = Math.Min(checked(progress.progress + 1), definition.requirement);
            progress.isNewAchieved = false;
            user.Data.userBeginnerMissionV2s = records.OrderBy(m => m.beginnerMissionV2Id).ToArray();
            user.MarkChanged(nameof(SuiteUser.userBeginnerMissionV2s));
            if (progress.progress == definition.requirement)
            {
                MarkMissionAchieved(BeginnerMissionV2Type, definition.id);
                user.MarkChanged(nameof(SuiteUser.userMissionStatuses));
            }
        }
    }

    private void RecordBeginnerMissionProgress(IEnumerable<MasterBeginnerMissionV2> definitions, int amount = 1)
    {
        foreach (var definition in definitions)
        {
            var missions = (user.Data.userBeginnerMissionV2s ?? []).ToList();
            var progress = missions.SingleOrDefault(m => m.beginnerMissionV2Id == definition.id);
            if (progress == null)
            {
                progress = new UserBeginnerMissionV2 { beginnerMissionV2Id = definition.id };
                missions.Add(progress);
            }
            progress.progress += amount;
            user.Data.userBeginnerMissionV2s = missions.OrderBy(m => m.beginnerMissionV2Id).ToArray();
            user.MarkChanged(nameof(SuiteUser.userBeginnerMissionV2s));
            if (progress.progress < definition.requirement ||
                user.Data.userMissionStatuses?.Any(s => s.missionType == BeginnerMissionV2Type &&
                    s.missionId == definition.id && s.missionStatus is "achieved" or "received") == true) continue;
            MarkMissionAchieved(BeginnerMissionV2Type, definition.id);
            user.MarkChanged(nameof(SuiteUser.userMissionStatuses));
        }
    }

    public UserResource[] ReceiveLiveMissionRewards(int[]? missionIds)
    {
        if (missionIds == null || missionIds.Length == 0 || missionIds.Any(id => id <= 0))
            throw new ArgumentException("Mission IDs are required.");
        var obtained = new List<UserResource>();
        foreach (var id in missionIds.Distinct())
        {
            var mission = master.GetLiveMission(id) ?? throw new ArgumentException("Unknown Live mission.");
            var status = user.Data.userMissionStatuses?.SingleOrDefault(s => s.missionType == "live_mission" && s.missionId == id)
                ?? throw new ArgumentException("Live mission is not achieved.");
            if (status.missionStatus == "received")
                continue;
            if (status.missionStatus != "achieved")
                throw new ArgumentException("Live mission is not achieved.");
            var progress = user.Data.userLiveMissions?.SingleOrDefault(m => m.liveMissionPeriodId == mission.liveMissionPeriodId)
                ?? throw new ArgumentException("Live mission progress is missing.");
            var entitled = mission.liveMissionType switch
            {
                "free" => true,
                "premium" => progress.liveMissionStatus is "premium" or "premium_and_mysekai",
                "mysekai" => progress.liveMissionStatus is "mysekai" or "premium_and_mysekai",
                _ => false
            };
            if (!entitled)
                throw new ArgumentException("Live mission pass is not owned.");
            if (mission.rewards == null || mission.rewards.Length == 0)
                throw new InvalidOperationException("Missing Live mission rewards.");
            foreach (var reward in mission.rewards)
            {
                var resources = resourceMaster.BuildResourcesFromBox("mission_reward", reward.resourceBoxId);
                if (resources.Length == 0)
                    throw new InvalidOperationException("Missing Live mission reward box.");
                resourceService.Grant(resources);
                obtained.AddRange(resources);
            }
            status.missionStatus = "received";
            user.MarkChanged(nameof(SuiteUser.userMissionStatuses));
        }
        return obtained.ToArray();
    }

    public (int Status, UserResource[] Rewards) ReceiveHonorMissionRewards(int[]? missionIds)
    {
        if (missionIds == null || missionIds.Length == 0 || missionIds.Any(id => id <= 0))
            throw new ArgumentException("缺少称号任务 ID。");
        if (missionIds.Distinct().Count() != missionIds.Length) return (409, []);
        var definitions = missionIds.Select(id => master.GetHonorMission(id)
            ?? throw new ArgumentException("未知称号任务。")).ToArray();
        var statuses = definitions.Select(m => user.Data.userMissionStatuses?
            .SingleOrDefault(s => s.missionType == "honor_mission" && s.missionId == m.id)).ToArray();
        if (statuses.Any(s => s?.missionStatus != "achieved")) return (409, []);
        var obtained = new List<UserResource>();
        foreach (var mission in definitions)
        {
            if (mission.rewards == null || mission.rewards.Length == 0)
                throw new InvalidOperationException("缺少称号任务奖励。");
            foreach (var reward in mission.rewards)
            {
                var resources = resourceMaster.BuildResourcesFromBox("mission_reward", reward.resourceBoxId);
                if (resources.Length == 0) throw new InvalidOperationException("缺少称号任务奖励箱。");
                resourceService.Grant(resources);
                obtained.AddRange(resources);
            }
        }
        foreach (var status in statuses) status!.missionStatus = "received";
        user.MarkChanged(nameof(SuiteUser.userMissionStatuses));
        return (200, obtained.ToArray());
    }

    public UserMissionReceiveResponse ReceiveBeginnerMissionV2Rewards(int[]? missionIds)
    {
        var obtainedRewards = new List<UserResource>();
        var normalReceived = 0;
        if (missionIds == null || missionIds.Length == 0)
        {
            return new UserMissionReceiveResponse
            {
                ObtainedRewards = []
            };
        }

        foreach (var missionId in missionIds.Where(id => id > 0).Distinct())
        {
            var mission = master.GetBeginnerMissionV2(missionId)
                ?? throw new ArgumentException("Unknown beginner mission.");
            var status = user.Data.userMissionStatuses?.SingleOrDefault(s =>
                s.missionType == BeginnerMissionV2Type && s.missionId == missionId);
            if (status?.missionStatus == "received")
                throw new MissionAlreadyReceivedException();
            if (status?.missionStatus != "achieved")
                throw new ArgumentException("Beginner mission is not achieved.");
            MarkMissionReceived(BeginnerMissionV2Type, missionId);
            if (mission.beginnerMissionV2Category == "normal") normalReceived++;
            foreach (var reward in mission?.rewards ?? [])
            {
                var resources = resourceMaster.BuildResourcesFromBox("mission_reward", reward.resourceBoxId);
                foreach (var resource in resources)
                {
                    resourceService.Grant(resource);
                    obtainedRewards.Add(resource);
                }
            }
        }

        if (normalReceived > 0)
        {
            foreach (var definition in master.GetBeginnerCompletionMissions())
            {
                var records = (user.Data.userBeginnerMissionV2s ?? []).ToList();
                var progress = records.SingleOrDefault(m => m.beginnerMissionV2Id == definition.id);
                var updated = checked((progress?.progress ?? 0) + normalReceived);
                if (updated >= definition.requirement)
                    throw new NotSupportedException("Beginner completion mission achievement is not verified.");
                if (progress == null)
                {
                    progress = new UserBeginnerMissionV2 { beginnerMissionV2Id = definition.id };
                    records.Add(progress);
                }
                progress.progress = updated;
                progress.isNewAchieved = false;
                user.Data.userBeginnerMissionV2s = records.OrderBy(m => m.beginnerMissionV2Id).ToArray();
            }
        }
        user.MarkChanged(nameof(SuiteUser.userBeginnerMissionV2s));
        user.MarkChanged(nameof(SuiteUser.userMissionStatuses));

        return new UserMissionReceiveResponse
        {
            ObtainedRewards = obtainedRewards.ToArray()
        };
    }

    public void TouchBeginnerMissionProgress(int missionId)
    {
        user.Data.userBeginnerMissionV2s ??= [];
        var missions = user.Data.userBeginnerMissionV2s.ToList();
        var mission = missions.FirstOrDefault(m => m.beginnerMissionV2Id == missionId);
        if (mission == null)
        {
            mission = new UserBeginnerMissionV2
            {
                beginnerMissionV2Id = missionId
            };
            missions.Add(mission);
        }

        var requirement = master.GetBeginnerMissionV2(missionId)?.requirement ?? 1;
        mission.progress = Math.Max(mission.progress, requirement);
        mission.isNewAchieved = true;
        user.Data.userBeginnerMissionV2s = missions.OrderBy(m => m.beginnerMissionV2Id).ToArray();
        MarkMissionAchieved(BeginnerMissionV2Type, missionId);
        user.MarkChanged(nameof(SuiteUser.userBeginnerMissionV2s));
        user.MarkChanged(nameof(SuiteUser.userMissionStatuses));
    }

    private void MarkMissionAchieved(string missionType, int missionId)
    {
        var status = GetOrCreateMissionStatus(missionType, missionId);
        if (!string.Equals(status.missionStatus, "received", StringComparison.Ordinal))
            status.missionStatus = "achieved";
    }

    private void MarkMissionReceived(string missionType, int missionId)
    {
        var status = GetOrCreateMissionStatus(missionType, missionId);
        status.missionStatus = "received";

        if (string.Equals(missionType, BeginnerMissionV2Type, StringComparison.Ordinal))
        {
            user.Data.userBeginnerMissionV2s ??= [];
            var missions = user.Data.userBeginnerMissionV2s.ToList();
            var mission = missions.FirstOrDefault(m => m.beginnerMissionV2Id == missionId);
            if (mission == null)
            {
                mission = new UserBeginnerMissionV2
                {
                    beginnerMissionV2Id = missionId
                };
                missions.Add(mission);
            }

            var requirement = master.GetBeginnerMissionV2(missionId)?.requirement ?? 1;
            mission.progress = Math.Max(mission.progress, requirement);
            mission.isNewAchieved = false;
            user.Data.userBeginnerMissionV2s = missions.OrderBy(m => m.beginnerMissionV2Id).ToArray();
        }
    }

    private UserMissionStatus GetOrCreateMissionStatus(string missionType, int missionId)
    {
        user.Data.userMissionStatuses ??= [];
        var statuses = user.Data.userMissionStatuses.ToList();
        var status = statuses.FirstOrDefault(s =>
            s.missionId == missionId &&
            string.Equals(s.missionType, missionType, StringComparison.Ordinal));

        if (status != null)
            return status;

        status = new UserMissionStatus
        {
            userId = user.UserId,
            missionType = missionType,
            missionId = missionId,
            missionStatus = "achieved"
        };
        statuses.Add(status);
        user.Data.userMissionStatuses = statuses
            .OrderBy(s => s.missionType, StringComparer.Ordinal)
            .ThenBy(s => s.missionId)
            .ToArray();
        return status;
    }

    public void UpdateLiveMissionProgress(UserLivePoint livePoint)
    {
        if (livePoint.liveMissionPeriodId <= 0 || livePoint.addNormalProgress <= 0)
            return;

        user.Data.userLiveMissions ??= [];
        var missions = user.Data.userLiveMissions.ToList();
        var mission = missions.FirstOrDefault(m =>
            m.liveMissionPeriodId == livePoint.liveMissionPeriodId &&
            string.Equals(m.liveMissionStatus, "free", StringComparison.Ordinal));

        if (mission == null)
        {
            mission = new UserLiveMission
            {
                userId = user.UserId,
                liveMissionPeriodId = livePoint.liveMissionPeriodId,
                liveMissionStatus = "free",
                achievedMissionIds = []
            };
            missions.Add(mission);
        }

        mission.progress = checked(mission.progress + livePoint.addNormalProgress);
        mission.achievedMissionIds ??= [];
        user.Data.userLiveMissions = missions.ToArray();
        user.MarkChanged(nameof(SuiteUser.userLiveMissions));
        foreach (var definition in master.GetFreeLiveMissions(mission.liveMissionPeriodId))
        {
            if (mission.progress < definition.requirement ||
                user.Data.userMissionStatuses?.Any(s => s.missionType == "live_mission" &&
                    s.missionId == definition.id && s.missionStatus is "achieved" or "received") == true)
                continue;
            MarkMissionAchieved("live_mission", definition.id);
            user.MarkChanged(nameof(SuiteUser.userMissionStatuses));
        }
    }
}

public sealed class MissionAlreadyReceivedException : Exception;
