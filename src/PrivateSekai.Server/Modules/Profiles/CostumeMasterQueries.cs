extern alias game;

using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Profiles;

public sealed class CostumeMasterQueries(MasterData master)
{
    public MasterCostume3D? GetCostume(int id) =>
        master.GetTable<MasterCostume3D>("costume3ds", c => c.id).FindById(id);
}
