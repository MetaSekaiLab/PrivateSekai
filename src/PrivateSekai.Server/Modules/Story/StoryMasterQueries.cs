extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Story;

public sealed class StoryMasterQueries(MasterData master)
{
    public MasterCardEpisode? GetMasterCardEpisode(int cardEpisodeId) =>
        master.GetTable<MasterCardEpisode>("cardEpisodes", e => e.id).FindById(cardEpisodeId);

    public int GetConfigInt(string configKey, int fallback = 0)
    {
        var config = master.GetTable<MasterConfig>("configs").Rows
            .FirstOrDefault(c => string.Equals(c.configKey, configKey, StringComparison.Ordinal));

        return int.TryParse(config?.value, out var value) ? value : fallback;
    }
}
