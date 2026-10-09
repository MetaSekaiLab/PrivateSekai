extern alias game;

using game::Sekai;
using PrivateSekai.Client.Realtime;

internal static class LiveAreaPowerChecks
{
    public static void Run(Action<bool, string> check)
    {
        var characters = new Dictionary<int, MasterGameCharacter>
        {
            [301] = new() { id = 301, unit = "light_sound" },
            [302] = new() { id = 302, unit = "piapro" },
            [303] = new() { id = 303, unit = "idol" }
        };
        var human = new MasterCard { id = 801, characterId = 301, supportUnit = "none", attr = "cool" };
        var singer = new MasterCard { id = 802, characterId = 302, supportUnit = "light_sound", attr = "cool" };
        var other = new MasterCard { id = 803, characterId = 303, supportUnit = "none", attr = "cute" };
        var same = Enumerable.Repeat(human, 5).ToArray();
        var singers = Enumerable.Repeat(singer, 5).ToArray();

        check(Power(human, same, []) == 0, "没有区域效果时不产生加成");
        var character = Effect(2);
        character.targetGameCharacterId = 301;
        character.targetUnit = "idol";
        check(Power(human, same, [character, Effect(1)]) == 9,
            "角色限定优先于组合和属性条件，通用效果另行累计");
        check(Power(other, same, [character]) == 0, "角色不匹配时不落入其他分支");
        check(Power(human, same, [Effect(0.6f), Effect(0.6f)]) == 3,
            "同类小数先累加，各属性最后取整");

        var unit = Effect(5, "light_sound", all: 10);
        check(Power(human, same, [unit]) == 30, "全员同组合使用替代倍率而非额外叠加");
        check(Power(human, same.Take(4).ToArray(), [unit]) == 15, "不足五人不触发全员匹配");
        unit.power3AllMatchBonusRate = null;
        check(Power(human, same, [unit]) == 15, "任一全员倍率缺失时三项统一使用普通倍率");
        var attr = Effect(2, attr: "cool", all: 1.9f);
        check(Power(human, same, [attr], 1000, 1000, 1000) == 30,
            "属性全员倍率先转整数，不按组合倍率处理");
        check(Power(human, same, [Effect(2, "light_sound", all: 1.9f)], 1000, 1000, 1000) == 57,
            "组合全员倍率保留小数");
        check(Power(human, [human, human, human, human, other], [attr]) == 6,
            "属性不齐时仅匹配成员使用普通属性加成");

        var own = Effect(10, "piapro", all: 20);
        var support = Effect(15, "light_sound", all: 30);
        check(Power(singer, singers, [own, support]) == 90, "所属与支援组合只保留较高一组");
        check(Power(singer, singers, [support, own]) == 90, "组合竞争不依赖输入先后顺序");
        check(Power(singer, singers, [own, support, Effect(15, "multi_unit")]) == 90,
            "混合倍率与全员额外倍率相等时保留全员效果");
        check(Power(singer, singers, [own, support, Effect(16, "multi_unit")]) == 93,
            "混合倍率较高时保留组合基础倍率并替换全员额外部分");

        var ownTie = Effect(0, "piapro");
        ownTie.power1BonusRate = 20;
        var supportTie = Effect(0, "light_sound");
        supportTie.power2BonusRate = 20;
        check(Power(singer, singers, [ownTie, supportTie], 100, 200, 300) == 20,
            "组合效果比较三项倍率之和，相等优先所属组合，不按实际属性选最大收益");

        check(Power(human, same, [Effect(7, "multi_unit")]) == 0, "单组合不触发混合效果");
        check(Power(human, [human, singer], [Effect(7, "multi_unit")]) == 21,
            "支援组合已存在时加入 piapro 参与混合判断");
        check(Power(singer, [singer], [Effect(7, "multi_unit")]) == 0,
            "单张带支援组合的 piapro 卡不直接视为两个组合");
        check(Power(human, [.. same, other], [Effect(5, "light_sound", all: 10), Effect(6, "multi_unit")]) == 33,
            "全员匹配只检查前五项，混合判断遍历输入列表");
        check(LiveAreaPower.Calculate(human, characters, same, [Effect(7, "multi_unit")], 100, 100, 100, 1) == 21
            && LiveAreaPower.Calculate(human, characters, [human, other], [Effect(7, "multi_unit")], 100, 100, 100, 2) == 0,
            "显式 multiUnitEval 与自动判断分开处理");

        int Power(MasterCard card, MasterCard[] deck, MasterAreaItemLevel[] effects,
            int one = 100, int two = 100, int three = 100) =>
            LiveAreaPower.Calculate(card, characters, deck, effects, one, two, three);
    }

    private static MasterAreaItemLevel Effect(float value, string unit = "any", string attr = "any", float? all = null) => new()
    {
        areaItemId = 901, level = 3, targetUnit = unit, targetCardAttr = attr,
        power1BonusRate = value, power2BonusRate = value, power3BonusRate = value,
        power1AllMatchBonusRate = all, power2AllMatchBonusRate = all, power3AllMatchBonusRate = all
    };
}
