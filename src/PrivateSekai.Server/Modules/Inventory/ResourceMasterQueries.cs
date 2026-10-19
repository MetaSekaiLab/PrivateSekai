extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Inventory;

public sealed class ResourceMasterQueries(MasterData master)
{
    public int[] FindSingleResourceBoxIds(string purpose, string type, int id) =>
        master.GetTable<MasterResourceBox>("resourceBoxes", b => b.id).Rows
            .Where(b => b.resourceBoxPurpose == purpose && b.details?.Length == 1 && b.details[0].resourceType == type &&
                b.details[0].resourceId == id && b.details[0].resourceQuantity == 1).Select(b => b.id).ToArray();

    public IReadOnlyList<MasterMusicVocal> GetMasterMusicVocals() =>
        master.GetTable<MasterMusicVocal>("musicVocals", v => v.id).Rows;

    public MasterMusicVocal? GetMasterMusicVocal(int musicVocalId) =>
        master.GetTable<MasterMusicVocal>("musicVocals", v => v.id).FindById(musicVocalId);

    public int GetMysekaiToolMaxDurability(int mysekaiToolId) =>
        master.GetTable<MasterMysekaiTool>("mysekaiTools", t => t.id)
            .FindById(mysekaiToolId)?.maxDurability ?? 0;

    public UserResource[] BuildResourcesFromBox(string purpose, int resourceBoxId)
    {
        if (resourceBoxId <= 0)
            return [];

        foreach (var box in master.GetTable<MasterResourceBox>("resourceBoxes", b => b.id).Rows)
        {
            if (!string.Equals(box.resourceBoxPurpose, purpose, StringComparison.Ordinal) ||
                box.id != resourceBoxId ||
                box.details == null)
                continue;

            return box.details
                .Select(detail => new UserResource
                {
                    resourceType = detail.resourceType,
                    resourceId = detail.resourceId,
                    resourceLevel = detail.resourceLevel,
                    quantity = detail.resourceQuantity
                })
                .ToArray();
        }

        return [];
    }

    public UserResource[] BuildResourcesFromBoxes(string purpose, IEnumerable<int>? resourceBoxIds)
    {
        if (resourceBoxIds == null)
            return [];

        return resourceBoxIds
            .SelectMany(resourceBoxId => BuildResourcesFromBox(purpose, resourceBoxId))
            .ToArray();
    }
}
