extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Missions;

public sealed class MissionMasterQueries(MasterData master)
{
    public MasterCharacterMissionV2[] GetAreaItemCharacterMissions(int areaItemId)
    {
        var characters = master.GetTable<MasterCharacterMissionV2AreaItem>("characterMissionV2AreaItems").Rows
            .Where(m => m.areaItemId == areaItemId && m.characterMissionType == "area_item_level_up_character")
            .Select(m => m.characterId).ToHashSet();
        return master.GetTable<MasterCharacterMissionV2>("characterMissionV2s").Rows
            .Where(m => m.characterMissionType == "area_item_level_up_character" && characters.Contains(m.characterId)).ToArray();
    }

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
        return condition == 0 ? [] : master.GetTable<MasterBeginnerMissionV2>("beginnerMissionV2s").Rows
            .Where(m => m.beginnerMissionV2Type == "read_unit_story" && m.conditionValue == condition).ToArray();
    }

    public MasterLiveMission? GetLiveMission(int id) =>
        master.GetTable<MasterLiveMission>("liveMissions", m => m.id).FindById(id);

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
