extern alias game;

using System.Globalization;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Home;

public sealed class FriendMasterQueries(MasterData master)
{
    public int Config(string key) => int.Parse(master.GetTable<MasterConfig>("configs").Rows
        .Single(c => c.configKey == key).value, CultureInfo.InvariantCulture);

    public bool HasNgWord(string message) => master.GetTable<MasterNGWord>("ngWords").Rows
        .Any(w => message.Contains(w.word, System.StringComparison.Ordinal));
}
