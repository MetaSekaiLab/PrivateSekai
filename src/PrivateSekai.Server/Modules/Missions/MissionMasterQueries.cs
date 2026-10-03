extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Missions;

public sealed class MissionMasterQueries(MasterData master)
{
    public MasterCharacterMissionV2[] GetCharacterMissions(int characterId) =>
        master.GetTable<MasterCharacterMissionV2>("characterMissionV2s", m => m.id).Rows
            .Where(m => m.characterId == characterId).ToArray();

    public MasterEventMission[] GetEventItemConsumptionMissions(int eventId) =>
        master.GetTable<MasterEventMission>("eventMissions").Rows
            .Where(m => m.eventId == eventId && m.eventMissionType == "consume_event_item").ToArray();

    public MasterCharacterMissionV2[] GetCardCollectionMissions(int characterId) =>
        master.GetTable<MasterCharacterMissionV2>("characterMissionV2s", m => m.id).Rows
            .Where(m => m.characterId == characterId && m.characterMissionType == "collect_member").ToArray();

    public MasterBeginnerMissionV2[] GetCardStoryMissions() =>
        master.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s", m => m.id).Rows
            .Where(m => m.beginnerMissionV2Type == "read_both_of_card_story").ToArray();

    public MasterCharacterMissionV2[] GetCardEpisodeMissions(int episodeId)
    {
        var episode = master.GetTable<MasterCardEpisode>("cardEpisodes", e => e.id).FindById(episodeId);
        var type = episode?.cardEpisodePartType switch
        {
            "first_part" => "read_card_episode_first",
            "second_part" => "read_card_episode_second",
            _ => null
        };
        if (type == null) return [];
        var card = master.GetTable<MasterCard>("cards", c => c.id).FindById(episode!.cardId);
        return card == null ? [] : master.GetTable<MasterCharacterMissionV2>("characterMissionV2s", m => m.id).Rows
            .Where(m => m.characterId == card.characterId && m.characterMissionType == type).ToArray();
    }

    public MasterBeginnerMissionV2[] GetCardLevelMissions() =>
        master.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s", m => m.id).Rows
            .Where(m => m.beginnerMissionV2Type == "any_card_level_up").ToArray();

    public MasterBeginnerMissionV2[] GetLiveClearMissions() =>
        master.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s", m => m.id).Rows
            .Where(m => m.beginnerMissionV2Type == "any_live_clear").ToArray();

    public MasterBeginnerMissionV2[] GetMusicPurchaseMissions() =>
        master.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s", m => m.id).Rows
            .Where(m => m.beginnerMissionV2Type == "exchange_any_music").ToArray();

    public MasterBeginnerMissionV2[] GetCostumeChangeMissions() =>
        master.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s", m => m.id).Rows
            .Where(m => m.beginnerMissionV2Type == "change_any_character_costume").ToArray();

    public MasterBeginnerMissionV2[] GetCostumeCraftMissions() =>
        master.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s", m => m.id).Rows
            .Where(m => m.beginnerMissionV2Type == "make_any_costume").ToArray();

    public MasterCharacterMissionV2[] GetCostumeCollectionMissions(int characterId) =>
        master.GetTable<MasterCharacterMissionV2>("characterMissionV2s", m => m.id).Rows
            .Where(m => m.characterId == characterId && m.characterMissionType == "collect_costume_3d").ToArray();

    public MasterHonorMission[] GetCostumeHonorMissions() =>
        master.GetTable<MasterHonorMission>("honorMissions", m => m.id).Rows
            .Where(m => m.honorMissionType == "collect_costume_3d").ToArray();

    public MasterBeginnerMissionV2[] GetBeginnerCompletionMissions() =>
        master.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s", m => m.id).Rows
            .Where(m => m.beginnerMissionV2Type == "achieve_all_missions").ToArray();

    public MasterBeginnerMissionV2[] GetAreaItemPurchaseMissions() =>
        master.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s", m => m.id).Rows
            .Where(m => m.beginnerMissionV2Type == "exchange_any_area_item").ToArray();

    public MasterCharacterMissionV2[] GetAreaItemCharacterMissions(int areaItemId)
    {
        var mappings = master.GetTable<MasterCharacterMissionV2AreaItem>("characterMissionV2AreaItems").Rows
            .Where(m => m.areaItemId == areaItemId).ToArray();
        var characters = mappings.Where(m => m.characterMissionType == "area_item_level_up_character")
            .Select(m => m.characterId).ToHashSet();
        var units = mappings.Where(m => m.characterMissionType == "area_item_level_up_unit")
            .Select(m => m.unit).ToHashSet();
        var unitCharacters = units.Count == 0 ? [] : master.GetTable<MasterGameCharacter>("gameCharacters").Rows
            .Where(c => units.Contains(c.unit)).Select(c => c.id).ToArray();
        return master.GetTable<MasterCharacterMissionV2>("characterMissionV2s", m => m.id).Rows
            .Where(m => (m.characterMissionType == "area_item_level_up_character" && characters.Contains(m.characterId)) ||
                (m.characterMissionType == "area_item_level_up_unit" && unitCharacters.Contains(m.characterId))).ToArray();
    }

    public string? GetCharacterMissionType(int missionId) =>
        master.GetTable<MasterCharacterMissionV2>("characterMissionV2s", m => m.id).FindById(missionId)?.characterMissionType;

    public MasterCharacterMissionV2ParameterGroup[] GetCharacterMissionParameters(int groupId) =>
        master.GetTable<MasterCharacterMissionV2ParameterGroup>("characterMissionV2ParameterGroups").Rows
            .Where(p => p.id == groupId).OrderBy(p => p.seq).ToArray();

    public MasterBeginnerMissionV2[] GetUnitStoryMissions(string? unit)
    {
        // read_unit_story 的 conditionValue 使用独立编号，不是 UnitType 枚举值。
        var condition = unit switch
        {
            "light_sound" => 1, "idol" => 2, "street" => 3,
            "theme_park" => 4, "school_refusal" => 5, "piapro" => 6,
            _ => 0
        };
        return condition == 0 ? [] : master.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s", m => m.id).Rows
            .Where(m => m.beginnerMissionV2Type == "read_unit_story" && m.conditionValue == condition).ToArray();
    }

    public MasterLiveMission? GetLiveMission(int id) =>
        master.GetTable<MasterLiveMission>("liveMissions", m => m.id).FindById(id);

    public MasterLiveMission[] GetFreeLiveMissions(int periodId) =>
        master.GetTable<MasterLiveMission>("liveMissions", m => m.id).Rows
            .Where(m => m.liveMissionPeriodId == periodId && m.liveMissionType == "free")
            .OrderBy(m => m.id).ToArray();

    public MasterBeginnerMissionV2? GetBeginnerMissionV2(int missionId) =>
        master.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s", m => m.id)
            .FindById(missionId);

    public int GetCurrentLiveMissionPeriodId()
    {
        var current = 0;
        foreach (var pass in master.GetTable<MasterLiveMissionPath>("liveMissionPasses", p => p.id).Rows)
            current = Math.Max(current, pass.liveMissionPeriodId);
        return current;
    }
}
