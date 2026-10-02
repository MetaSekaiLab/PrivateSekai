extern alias game;

using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Gacha;

public sealed class GachaMasterQueries(MasterData master)
{
    public MasterGacha? GetMasterGacha(int gachaId) =>
        master.GetTable<MasterGacha>("gachas", g => g.id).FindById(gachaId);

    public MasterGachaBehavior? GetMasterGachaBehavior(MasterGacha? gacha, int gachaBehaviorId) =>
        gacha?.gachaBehaviors?.FirstOrDefault(b => b.id == gachaBehaviorId);

    public int ResolveGachaCeilItemId(MasterGacha? gacha, int gachaId)
    {
        if (gacha?.gachaCeilItemId > 0)
            return gacha.gachaCeilItemId;

        return GetMasterGacha(gachaId)?.gachaCeilItemId ?? 0;
    }

    public MasterGachaCeilExchange? GetMasterGachaCeilExchange(int gachaCeilExchangeId)
    {
        foreach (var summary in master.GetTable<MasterGachaCeilExchangeSummary>("gachaCeilExchangeSummaries", s => s.id).Rows)
        {
            var exchange = summary.gachaCeilExchanges?.FirstOrDefault(e => e.id == gachaCeilExchangeId);
            if (exchange != null)
                return exchange;
        }

        return null;
    }
}
