extern alias game;

using System;
using System.Globalization;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Gacha;

public sealed class GachaMasterQueries(MasterData master)
{
    public MasterGacha? GetMasterGacha(int gachaId) =>
        master.GetTable<MasterGacha>("gachas", g => g.id).FindById(gachaId);

    public MasterGachaBehavior? GetMasterGachaBehavior(MasterGacha? gacha, int gachaBehaviorId) =>
        gacha?.gachaBehaviors?.FirstOrDefault(b => b.id == gachaBehaviorId);

    public MasterColorfulPassV2 GetColorfulPass(int id) =>
        master.GetTable<MasterColorfulPassV2>("colorfulPassV2s", p => p.id).FindById(id)
        ?? throw new InvalidOperationException("Missing colorful pass definition.");

    public int GetDayChangeHour()
    {
        var hour = int.Parse(master.GetTable<MasterConfig>("configs").Rows.Single(c => c.configKey == "date_change_hour").value,
            CultureInfo.InvariantCulture);
        return hour is >= 0 and <= 23 ? hour : throw new InvalidOperationException("Invalid day change hour.");
    }

    public MasterGachaBonus GetGachaBonus(int id) =>
        master.GetTable<MasterGachaBonus>("gachaBonuses", b => b.id).FindById(id)
        ?? throw new InvalidOperationException("Missing gacha bonus definition.");

    public MasterGachaBonusItemReceivableRewardGroup[] GetItemReceivableRewards(int groupId)
    {
        var rewards = master.GetTable<MasterGachaBonusItemReceivableRewardGroup>("gachaBonusItemReceivableRewards", r => r.id).Rows
            .Where(r => r.groupId == groupId).OrderBy(r => r.gachaBonusBorderPoint).ToArray();
        if (rewards.Length == 0 || rewards.Any(r => r.gachaBonusBorderPoint <= 0
                || r.gachaBonusRewardType is not ("item" or "not_owned_random_rarity4_fixed" or "selectable_rarity4_fixed" or "random_item" or "selectable_item"))
            || rewards.Select(r => r.gachaBonusBorderPoint).Distinct().Count() != rewards.Length)
            throw new InvalidOperationException("Invalid item receivable gacha rewards.");
        return rewards;
    }

    public int[] GetBonusCardSupplyIds(int groupId)
    {
        var ids = master.GetTable<MasterCardSupplyGroup>("cardSupplyGroups", s => s.id).Rows
            .Where(s => s.groupId == groupId).Select(s => s.cardSupplyId).Distinct().ToArray();
        return ids.Length > 0 && ids.All(id => id > 0) ? ids : throw new InvalidOperationException("Missing bonus card supplies.");
    }

    public MasterGachaBonusRewardItemGroup[] GetBonusRewardItems(int groupId)
    {
        var items = master.GetTable<MasterGachaBonusRewardItemGroup>("gachaBonusRewardItemGroups", i => i.id).Rows
            .Where(i => i.groupId == groupId).OrderBy(i => i.seq).ToArray();
        return items.Length > 0 && items.All(i => i.weight >= 0 && i.resourceBoxId > 0)
            ? items : throw new InvalidOperationException("Invalid bonus reward items.");
    }

    public MasterResourceBox GetBonusResourceBox(int id)
    {
        var box = master.GetTable<MasterResourceBox>("resourceBoxes", b => b.id).Rows
            .SingleOrDefault(b => b.resourceBoxPurpose == "gacha_bonus_item_receivable_reward" && b.id == id)
            ?? throw new InvalidOperationException("Missing gacha bonus resource box.");
        if (box.resourceBoxType is not ("expand" or "list") || box.details == null || box.details.Length == 0
            || box.details.Any(d => d.resourceQuantity <= 0 || string.IsNullOrEmpty(d.resourceType)
                || d.resourceId <= 0 && d.resourceType is not ("coin" or "virtual_coin" or "jewel")))
            throw new InvalidOperationException("Invalid gacha bonus resource box.");
        return box;
    }

    public MasterRateChoiceGachaWish[] GetRateChoiceWishes(int groupId)
    {
        var wishes = master.GetTable<MasterRateChoiceGachaWish>("rateChoiceGachaWishes", w => w.id).Rows
            .Where(w => w.groupId == groupId).OrderBy(w => w.seq).ToArray();
        if (wishes.Length == 0 || wishes.Any(w => w.selectCount <= 0
            || w.lotteryType is not ("rate_choice_first" or "rate_choice_second"))
            || wishes.Select(w => w.lotteryType).Distinct().Count() != wishes.Length)
            throw new InvalidOperationException("Invalid rate choice gacha wishes.");
        return wishes;
    }

    public MasterGiftGachaExchange? GetGiftExchange(int gachaId, int exchangeId) =>
        master.GetTable<MasterGiftGachaExchange>("giftGachaExchanges", e => e.id).FindById(exchangeId) is { } exchange
            && exchange.gachaId == gachaId ? exchange : null;

    public MasterResourceBox GetGiftResourceBox(int id)
    {
        var box = master.GetTable<MasterResourceBox>("resourceBoxes", b => b.id).Rows
            .SingleOrDefault(b => b.resourceBoxPurpose == "gift_gacha_extra" && b.id == id)
            ?? throw new InvalidOperationException("Missing gift gacha resource box.");
        if (box.resourceBoxType is not ("expand" or "list") || box.details == null || box.details.Length == 0
            || box.details.Any(d => d.resourceQuantity <= 0 || string.IsNullOrEmpty(d.resourceType)
                || d.resourceId <= 0 && d.resourceType != "coin"))
            throw new InvalidOperationException("Invalid gift gacha resource box.");
        return box;
    }

    public MasterGachaFreebieGroup[] GetFreebieGroups(int groupId)
    {
        var groups = master.GetTable<MasterGachaFreebieGroup>("gachaFreebieGroups", g => g.id).Rows
            .Where(g => g.groupId == groupId).OrderBy(g => g.seq).ToArray();
        if (groups.Any(g => g.weight < 0 || g.rarity <= 0) || !groups.Any(g => g.weight > 0))
            throw new InvalidOperationException("Invalid gacha freebie group.");
        return groups.Where(g => g.weight > 0).ToArray();
    }

    public MasterResourceBox GetFreebieResourceBox(int id)
    {
        var box = master.GetTable<MasterResourceBox>("resourceBoxes", b => b.id).Rows
            .SingleOrDefault(b => b.resourceBoxPurpose == "gacha_freebie_group" && b.id == id)
            ?? throw new InvalidOperationException("Missing gacha freebie resource box.");
        if (box.resourceBoxType != "expand" || box.details == null || box.details.Length == 0
            || box.details.Any(d => d.resourceQuantity <= 0 || string.IsNullOrEmpty(d.resourceType)
                || d.resourceId <= 0 && d.resourceType is not ("coin" or "virtual_coin" or "jewel")))
            throw new InvalidOperationException("Invalid gacha freebie resource box.");
        return box;
    }

    public MasterResourceBox GetExtraResourceBox(int extraId)
    {
        var extra = master.GetTable<MasterGachaExtra>("gachaExtras", e => e.id).FindById(extraId)
            ?? throw new InvalidOperationException("Missing gacha extra definition.");
        var box = master.GetTable<MasterResourceBox>("resourceBoxes", b => b.id).Rows
            .SingleOrDefault(b => b.resourceBoxPurpose == "gacha_extra" && b.id == extra.resourceBoxId)
            ?? throw new InvalidOperationException("Missing gacha extra resource box.");
        if (box.resourceBoxType is not ("expand" or "costume_3d") || box.details == null || box.details.Length == 0
            || box.details.Any(d => d.resourceQuantity <= 0 || string.IsNullOrEmpty(d.resourceType))
            || box.resourceBoxType == "costume_3d" && box.details.Any(d => d.resourceType != "costume_3d"))
            throw new InvalidOperationException("Invalid gacha extra resource box.");
        return box;
    }

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
