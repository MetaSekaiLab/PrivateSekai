extern alias game;

using game::Sekai;

namespace PrivateSekai.Client.Realtime;

public static class LiveAreaPower
{
    // effects 为用户实际持有等级对应的 master 行，保持原顺序。
    public static int Calculate(MasterCard card, IReadOnlyDictionary<int, MasterGameCharacter> characters,
        IReadOnlyList<MasterCard> deck, IEnumerable<MasterAreaItemLevel> effects,
        int power1, int power2, int power3, int multiUnitEval = 0)
    {
        var buffs = new Dictionary<string, Rates>();
        var bases = new Dictionary<string, Rates>();
        var mixed = multiUnitEval switch { 1 => true, 2 => false, _ => IsMixed(deck, characters) };
        var unit = characters[card.characterId].unit;
        foreach (var effect in effects)
        {
            var normal = new Rates(effect.power1BonusRate, effect.power2BonusRate, effect.power3BonusRate);
            if (effect.targetGameCharacterId != 0)
            {
                if (effect.targetGameCharacterId == card.characterId) Add(buffs, "character", normal);
            }
            else if (effect.targetUnit == "multi_unit")
            {
                if (mixed) Add(buffs, "multi", normal);
            }
            else if (effect.targetUnit != "any")
            {
                var key = effect.targetUnit == unit ? "unit" : "support";
                var target = key == "unit" ? unit : card.supportUnit;
                if ((key == "support" && target == "none") || target != effect.targetUnit) continue;
                var all = FirstFiveMatch(deck, c => characters[c.characterId].unit == effect.targetUnit
                    || c.supportUnit == effect.targetUnit);
                Add(buffs, key, MatchRates(effect, all, false));
                Add(bases, key, normal);
            }
            else if (effect.targetCardAttr != "any")
            {
                if (effect.targetCardAttr != card.attr) continue;
                var all = FirstFiveMatch(deck, c => c.attr == effect.targetCardAttr);
                Add(buffs, "attr", MatchRates(effect, all, true));
            }
            else Add(buffs, "any", normal);
        }

        if (buffs.TryGetValue("unit", out var unitBuff) && buffs.TryGetValue("support", out var supportBuff))
        {
            var removed = unitBuff.Sum < supportBuff.Sum ? "unit" : "support";
            buffs.Remove(removed);
            bases.Remove(removed);
        }
        if (buffs.TryGetValue("multi", out var multi))
        {
            var key = buffs.ContainsKey("unit") ? "unit" : buffs.ContainsKey("support") ? "support" : null;
            if (key != null)
            {
                var value = buffs[key];
                var basic = bases.GetValueOrDefault(key);
                var extra = new Rates(value.One - basic.One, value.Two - basic.Two, value.Three - basic.Three);
                if (extra.Sum >= multi.Sum) buffs.Remove("multi");
                else buffs[key] = basic;
            }
        }

        float one = 0, two = 0, three = 0;
        foreach (var value in buffs.Values)
        {
            one += (float)(value.One * 0.01) * power1;
            two += (float)(value.Two * 0.01) * power2;
            three += (float)(value.Three * 0.01) * power3;
        }
        return (int)MathF.Floor(one) + (int)MathF.Floor(two) + (int)MathF.Floor(three);
    }

    private static Rates MatchRates(MasterAreaItemLevel effect, bool all, bool truncate)
    {
        if (all && effect.power1AllMatchBonusRate is float one
            && effect.power2AllMatchBonusRate is float two && effect.power3AllMatchBonusRate is float three)
            return truncate ? new((int)one, (int)two, (int)three) : new(one, two, three);
        return new(effect.power1BonusRate, effect.power2BonusRate, effect.power3BonusRate);
    }

    private static bool FirstFiveMatch(IReadOnlyList<MasterCard> deck, Func<MasterCard, bool> match) =>
        deck.Count >= 5 && deck.Take(5).All(c => c != null && match(c));

    private static bool IsMixed(IReadOnlyList<MasterCard> deck, IReadOnlyDictionary<int, MasterGameCharacter> characters)
    {
        var units = new HashSet<string>();
        var supports = new List<string>();
        foreach (var member in deck)
        {
            if (member == null) continue;
            var unit = characters[member.characterId].unit;
            if (unit == "piapro") supports.Add(member.supportUnit);
            else units.Add(unit);
        }
        var piapro = false;
        foreach (var support in supports)
        {
            if (string.IsNullOrEmpty(support) || support == "none" || units.Contains(support)) piapro = true;
            else units.Add(support);
        }
        if (piapro) units.Add("piapro");
        return units.Count > 1;
    }

    private static void Add(Dictionary<string, Rates> values, string key, Rates value)
    {
        var old = values.GetValueOrDefault(key);
        values[key] = new(old.One + value.One, old.Two + value.Two, old.Three + value.Three);
    }

    private readonly record struct Rates(float One, float Two, float Three)
    {
        public float Sum => One + Two + Three;
    }
}
