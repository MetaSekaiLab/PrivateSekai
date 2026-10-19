extern alias game;

using System;
using System.Globalization;
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

    public MasterGachaBonus GetGachaBonus(int id) =>
        master.GetTable<MasterGachaBonus>("gachaBonuses", b => b.id).FindById(id)
        ?? throw new InvalidOperationException("Missing gacha bonus definition.");

    public float GetGachaBonusPointRate(bool paid)
    {
        var config = master.GetTable<MasterClientConfig>("clientConfigs", c => c.id).FindById(paid ? 114 : 113)
            ?? throw new InvalidOperationException("Missing gacha bonus point rate.");
        var rate = float.Parse(config.value, CultureInfo.InvariantCulture);
        if (config.type != "Float" || !float.IsFinite(rate) || rate <= 0)
            throw new InvalidOperationException("Invalid gacha bonus point rate.");
        return rate;
    }

    public int ResolveGachaCeilItemId(MasterGacha? gacha, int gachaId)
    {
        if (gacha?.gachaCeilItemId > 0)
            return gacha.gachaCeilItemId;

        return GetMasterGacha(gachaId)?.gachaCeilItemId ?? 0;
    }

    public (MasterGachaCeilExchangeSummary Summary, MasterGachaCeilExchange Exchange)? FindGachaCeilExchange(int gachaCeilExchangeId)
    {
        foreach (var summary in master.GetTable<MasterGachaCeilExchangeSummary>("gachaCeilExchangeSummaries", s => s.id).Rows)
        {
            var exchange = summary.gachaCeilExchanges?.FirstOrDefault(e => e.id == gachaCeilExchangeId);
            if (exchange != null)
                return (summary, exchange);
        }

        return null;
    }
}
