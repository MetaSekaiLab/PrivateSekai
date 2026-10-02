extern alias game;

using System;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Missions;

public sealed class MissionMasterQueries(MasterData master)
{
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
