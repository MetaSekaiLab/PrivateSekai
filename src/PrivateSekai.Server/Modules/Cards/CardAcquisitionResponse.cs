extern alias game;

using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Missions;

namespace PrivateSekai.Modules.Cards;

public sealed class CardAcquisitionResponse(SuiteUser data)
{
    private readonly HashSet<int> honors = HonorMissionResponse.AchievedIds(data);
    private readonly HashSet<int> released = (data.userReleaseConditions ?? [])
        .Select(c => c.releaseConditionId).ToHashSet();
    private readonly HashSet<(int, int, int, int)> statuses = (data.userCharacterMissionStatuses ?? [])
        .Select(s => (s.characterId, s.missionId, s.parameterGroupId, s.seq)).ToHashSet();

    public void AddHints(SuiteUser refresh, MissionMasterQueries master)
    {
        var newConditions = (data.userReleaseConditions ?? []).Where(c => !released.Contains(c.releaseConditionId)).ToArray();
        if (newConditions.Length > 0) refresh.newReleaseConditions = newConditions;
        HonorMissionResponse.AddAchievementHints(refresh, honors, master);
        if (refresh.userCharacterMissions == null) return;
        var achieved = (data.userCharacterMissionStatuses ?? [])
            .Where(s => !statuses.Contains((s.characterId, s.missionId, s.parameterGroupId, s.seq))).ToArray();
        var definitions = achieved.Select(s => s.characterId).Distinct().SelectMany(master.GetCharacterMissions).ToDictionary(m => m.id);
        refresh.userCharacterMissions = refresh.userCharacterMissions.Select(m => new UserCharacterMissionV2
        {
            userId = m.userId, characterId = m.characterId, characterMissionType = m.characterMissionType,
            progress = m.progress, achievedMissions = achieved.Where(s => s.characterId == m.characterId &&
                definitions[s.missionId].characterMissionType == m.characterMissionType).ToArray()
        }).ToArray();
    }
}
