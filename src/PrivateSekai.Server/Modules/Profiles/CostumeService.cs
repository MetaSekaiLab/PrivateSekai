extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Profiles;

public sealed class CostumeService(UserSession user, CostumeMasterQueries master, MissionService missions)
{
    public void Save(int characterId, string unit, UserCharacterCostume3DRequest request)
    {
        var current = user.Data.userCharacterCostume3ds?.SingleOrDefault(c => c.characterId == characterId &&
            c.unit.ToUpperInvariant() == unit) ?? throw new ArgumentException("Unknown character costume slot.");
        foreach (var (id, part) in new[] { (request.headCostume3dId, "head"), (request.bodyCostume3dId, "body"), (request.hairCostume3dId, "hair") })
        {
            var costume = master.GetCostume(id) ?? throw new ArgumentException("Unknown costume.");
            if (costume.characterId != characterId || costume.partType != part)
                throw new ArgumentException("Costume does not match character or part.");
            if (user.Data.userCostume3dStatuses?.Any(c => c.costume3dId == id && c.status == "available") != true)
                throw new ArgumentException("Costume is not available.");
        }
        current.headCostume3dId = request.headCostume3dId;
        current.bodyCostume3dId = request.bodyCostume3dId;
        current.hairCostume3dId = request.hairCostume3dId;
        user.MarkChanged(nameof(SuiteUser.userCharacterCostume3ds));
        missions.RecordCostumeChange();
    }
}
