extern alias game;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using game::Sekai;
using game::Sekai.ApiData;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Gacha;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class ItemReceivableGachaChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "item-receivable-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string json) => File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        var pool = JsonNode.Parse("""
            {"id":1,"startAt":1,"gachaBonusItemReceivableRewardGroupId":1,"gachaCeilItemId":9,
             "gachaDetails":[{"id":102,"cardId":2,"weight":1},{"id":103,"cardId":3,"weight":1},
              {"id":104,"cardId":4,"weight":999},{"id":105,"cardId":5,"weight":1},
              {"id":106,"cardId":6,"weight":2},{"id":107,"cardId":7,"weight":1},{"id":108,"cardId":8,"weight":1}],
             "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":50},
              {"cardRarityType":"rarity_3","lotteryType":"normal","rate":30},{"cardRarityType":"rarity_4","lotteryType":"normal","rate":20}],
             "gachaBehaviors":[{"id":1,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300},
              {"id":2,"spinCount":10,"gachaBehaviorType":"over_rarity_3_once","costResourceType":"jewel","costResourceQuantity":3000},
              {"id":3,"spinCount":10,"gachaBehaviorType":"over_rarity_3_once","costResourceType":"paid_jewel","costResourceQuantity":1500},
              {"id":4,"spinCount":1,"costResourceType":"gacha_ticket","costResourceId":1,"costResourceQuantity":1},
              {"id":5,"spinCount":1,"resourceCategory":"free_resource"}]}
            """)!;
        var pools = new JsonArray(pool);
        foreach (var id in Enumerable.Range(2, 12))
        {
            var clone = pool.DeepClone();
            clone["id"] = id;
            clone["gachaBonusItemReceivableRewardGroupId"] = id switch
                { 2 => 2, 3 => 0, 4 => 999, 5 => 3, 6 => 5, 7 => 6, 8 => 8, 9 => 7, 10 => 1, 11 => 4, 12 => 9, _ => 10 };
            if (id == 10) clone["gachaBonusId"] = 1;
            pools.Add(clone);
        }
        Table("gachas", pools.ToJsonString());
        Table("cards", """
            [{"id":2,"characterId":1,"cardRarityType":"rarity_2","cardSupplyId":1},{"id":3,"characterId":1,"cardRarityType":"rarity_3","cardSupplyId":1},
             {"id":4,"characterId":1,"cardRarityType":"rarity_4","cardSupplyId":1},{"id":5,"characterId":1,"cardRarityType":"rarity_4","cardSupplyId":1},
             {"id":6,"characterId":1,"cardRarityType":"rarity_4","cardSupplyId":3},{"id":7,"characterId":1,"cardRarityType":"rarity_4","cardSupplyId":4},
             {"id":8,"characterId":1,"cardRarityType":"rarity_birthday","cardSupplyId":1},{"id":9,"characterId":1,"cardRarityType":"rarity_4","cardSupplyId":1}]
            """);
        Table("cardSupplyGroups", """[{"id":1,"groupId":1,"cardSupplyId":1},{"id":2,"groupId":2,"cardSupplyId":1},{"id":3,"groupId":2,"cardSupplyId":3},{"id":4,"groupId":2,"cardSupplyId":4}]""");
        Table("clientConfigs", """[{"id":113,"type":"Float","value":"0.5"},{"id":114,"type":"Float","value":"1"}]""");
        Table("gachaBonusItemReceivableRewards", """
            [{"id":1,"groupId":1,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"item","resourceBoxId":1},
             {"id":2,"groupId":1,"gachaBonusBorderPoint":50,"gachaBonusRewardType":"not_owned_random_rarity4_fixed","cardSupplyGroupId":1},
             {"id":3,"groupId":1,"gachaBonusBorderPoint":100,"gachaBonusRewardType":"not_owned_random_rarity4_fixed","cardSupplyGroupId":2},
             {"id":4,"groupId":1,"gachaBonusBorderPoint":150,"gachaBonusRewardType":"selectable_rarity4_fixed","cardSupplyGroupId":1},
             {"id":5,"groupId":1,"gachaBonusBorderPoint":200,"gachaBonusRewardType":"selectable_rarity4_fixed","cardSupplyGroupId":2},
             {"id":6,"groupId":2,"gachaBonusBorderPoint":50,"gachaBonusRewardType":"item","resourceBoxId":2},
             {"id":7,"groupId":2,"gachaBonusBorderPoint":100,"gachaBonusRewardType":"item","resourceBoxId":2},
             {"id":8,"groupId":3,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"item","resourceBoxId":1},
             {"id":9,"groupId":3,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"item","resourceBoxId":1},
             {"id":10,"groupId":4,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"item","resourceBoxId":3},
             {"id":11,"groupId":5,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"item","resourceBoxId":999},
             {"id":12,"groupId":6,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"item","resourceBoxId":1},
             {"id":13,"groupId":6,"gachaBonusBorderPoint":50,"gachaBonusRewardType":"not_owned_random_rarity4_fixed","cardSupplyGroupId":999},
             {"id":14,"groupId":7,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"unknown"},
             {"id":15,"groupId":8,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"random_item","gachaBonusRewardItemGroupId":1},
             {"id":16,"groupId":8,"gachaBonusBorderPoint":20,"gachaBonusRewardType":"selectable_item","gachaBonusRewardItemGroupId":1},
             {"id":17,"groupId":8,"gachaBonusBorderPoint":30,"gachaBonusRewardType":"item","resourceBoxId":1},
             {"id":18,"groupId":9,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"random_item","gachaBonusRewardItemGroupId":2},
             {"id":19,"groupId":10,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"random_item","gachaBonusRewardItemGroupId":3}]
            """);
        Table("gachaBonusRewardItemGroups", """
            [{"id":11,"groupId":1,"seq":1,"resourceBoxId":1,"weight":1},{"id":12,"groupId":1,"seq":2,"resourceBoxId":2,"weight":3},
             {"id":13,"groupId":1,"seq":3,"resourceBoxId":3,"weight":0},{"id":14,"groupId":2,"seq":1,"resourceBoxId":1,"weight":0},
             {"id":15,"groupId":3,"seq":1,"resourceBoxId":1,"weight":-1}]
            """);
        Table("resourceBoxes", """
            [{"id":1,"resourceBoxPurpose":"gacha_bonus_item_receivable_reward","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":50}]},
             {"id":2,"resourceBoxPurpose":"gacha_bonus_item_receivable_reward","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":166,"resourceQuantity":1}]},
             {"id":3,"resourceBoxPurpose":"gacha_bonus_item_receivable_reward","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":3},{"resourceType":"material","resourceId":16,"resourceQuantity":2}]},
             {"id":1,"resourceBoxPurpose":"gacha_extra","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":999}]}]
            """);
        Table("characterMissionV2s", """[{"id":1,"characterId":1,"characterMissionType":"collect_member","parameterGroupId":1},{"id":2,"characterId":1,"characterMissionType":"waiting_room","parameterGroupId":1}]""");
        Table("characterMissionV2ParameterGroups", """[{"id":1,"seq":1,"requirement":1}]""");
        foreach (var table in new[] { "cardEpisodes", "cardCostume3ds", "costume3dShopItems", "releaseConditions", "characterArchiveVoices", "beginnerMissionV2s", "honorMissions" }) Table(table, "[]");
        var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
        var store = new MemoryUserStore();
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        var random = new DrawRandom();
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<IUserStore>(store)
            .AddSingleton<Random>(random).AddSingleton<TimeProvider>(new Clock()).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        void Reset(float point = 0, int id = 1, int[]? owned = null, bool existing = true)
        {
            var state = TestUsers.Create(1);
            state.Data.userChargedCurrency = new() { free = 10000, paid = 10000 };
            state.Data.userGachaTickets = [new() { gachaTicketId = 1, quantity = 1000 }];
            state.Data.userCards = (owned ?? []).Select(card => new UserCard { userId = 1, cardId = card, level = 1, skillLevel = 1 }).ToArray();
            state.Data.userGachaBonusPoints = existing ? [new() { userId = 1, gachaId = id, gachaBonusPoint = point, totalGachaBonusPoint = point }] : [];
            store.Save(1, state);
            random.Values.Clear();
            random.Pick = 0;
            random.Calls = 0;
        }
        UserGachaResponse Draw(int behavior = 1, int id = 1, int? count = null, int? card = null, int? item = null, bool paidFirst = false)
            => DumpSerializer.Deserialize<UserGachaResponse>(operation.Execute(1, () =>
        {
            var response = gacha.ExecuteGacha(id, behavior, paidFirst, count, card, item);
            response.updatedResources = user.BuildRefresh(prepareRefresh: false);
            return response;
        }));
        int Material(int id) => store.Read(1)!.Data.userMaterials.SingleOrDefault(m => m.materialId == id)?.quantity ?? 0;
        float Point(int id = 1) => store.Read(1)!.Data.userGachaBonusPoints.Single(p => p.gachaId == id).gachaBonusPoint;
        void Rejected(Action action, int status)
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            var error = Check.Throws<GachaRequestRejectedException>(action, "非法保底选择拒绝");
            Check.That(error.Status == status && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "拒绝回滚费用、点数、奖励和全部用户状态");
        }
        Reset(existing: false);
        Draw();
        Check.That(Point() == .5f && store.Read(1)!.Data.userGachaBonusPoints.Single().totalGachaBonusPoint == .5f, "首次普通水晶抽取创建本池半点保底进度");
        foreach (var entry in new[] { (1, .5f), (2, 5f), (3, 10f), (4, .5f), (5, 0f) })
        {
            Reset();
            Draw(entry.Item1);
            Check.That(Point() == entry.Item2, "普通、付费、票券与免费抽取按实际消耗累计点数");
        }
        Reset(9.5f);
        var mixed = store.Read(1)!;
        mixed.Data.userChargedCurrency.free = 150;
        store.Save(1, mixed);
        var firstItem = Draw();
        Check.That(Point() == 10 && Material(15) == 50 && firstItem.obtainGachaBonusItems.Single().quantity == 50
            && store.Read(1)!.Data.userChargedCurrency.free == 0 && store.Read(1)!.Data.userChargedCurrency.paid == 9850,
            "单抽混合付费与免费水晶仍只加半点，达到门槛发正确purpose的整箱附赠");
        Reset();
        mixed = store.Read(1)!;
        mixed.Data.userChargedCurrency.free = 450;
        store.Save(1, mixed);
        Draw(2);
        Check.That(Point() == 9, "十连只有完全付费的八抽计一点，含混合的两抽各半点");
        Reset(49.5f, 2);
        Draw(id: 2);
        Check.That(Point(2) == 50 && Material(166) == 1, "道具池50点实际入库");
        Reset(99.5f, 2);
        Draw(id: 2);
        Check.That(Point(2) == 0 && store.Read(1)!.Data.userGachaBonusPoints.Single().totalGachaBonusPoint == 100 && Material(166) == 1,
            "道具池按100点循环并保留累计点数");
        Reset(99, 2);
        Draw(3, 2);
        Check.That(Point(2) == 9 && Material(166) == 1, "跨循环余下九点保留，不再次补发旧门槛");
        Reset(45, 2);
        var many = Draw(4, 2, 400);
        Check.That(many.obtainPrizes.Length == 400 && many.obtainGachaBonusItems.Length == 4 && Material(166) == 4
            && Point(2) == 45 && store.Read(1)!.Data.userGachaTickets.Single().quantity == 600,
            "指定票券多抽跨两个完整循环，所有门槛各结算一次且费用不重复");
        Reset(49.5f, owned: [4]);
        var unowned = Draw();
        Check.That(unowned.obtainPrizes.Single().card.resourceId == 5 && unowned.obtainPrizes.Single().newFlg
            && unowned.obtainPrizes.Single().gachaLotteryType == "fixed_gacha_bonus", "未持有普通四星优先，排除已持有和其他供给类型");
        Reset(49.5f, owned: [4, 5]);
        random.Pick = 1;
        var fallback = Draw();
        Check.That(fallback.obtainPrizes.Single().card.resourceId == 5 && !fallback.obtainPrizes.Single().newFlg,
            "目标普通四星均持有时回退全部供给候选，档内等权而非999比1");
        Reset(99.5f, owned: [4, 5, 6]);
        Check.That(Draw().obtainPrizes.Single().card.resourceId == 7, "百点供给组可包含限定／节日，仍排除已持有、非四星和池外卡");
        Reset(149.5f);
        Rejected(() => Draw(), 409);
        foreach (var card in new[] { -1, 0, 3, 6, 8, 9, 999 }) Rejected(() => Draw(card: card), 400);
        Rejected(() => Draw(card: 5, item: 11), 400);
        Reset(149.5f, owned: [5]);
        var selected = Draw(card: 5);
        Check.That(selected.obtainPrizes.Single().card.resourceId == 5 && !selected.obtainPrizes.Single().newFlg
            && Point() == 150 && store.Read(1)!.Data.userCards.Single().duplicateCount == 1, "150点选卡可选已持有的合法普通四星并结算重复卡");
        Reset(199.5f);
        var final = Draw(card: 6);
        Check.That(final.obtainPrizes.Single().card.resourceId == 6 && Point() == 0
            && store.Read(1)!.Data.userGachaBonusPoints.Single().totalGachaBonusPoint == 200, "200点选卡使用更宽供给组并归零，累计点数保留");
        Rejected(() => Draw(card: 6), 400);
        Reset();
        Rejected(() => Draw(card: 4), 400);
        Rejected(() => Draw(item: 11), 400);
        Rejected(() => Draw(5, card: 4), 400);
        Reset(id: 3);
        Rejected(() => Draw(id: 3, card: 4), 400);
        Reset(49.5f);
        random.Values = new(Enumerable.Repeat(0d, 10));
        var separate = Draw(3);
        Check.That(separate.obtainPrizes.Length == 10 && separate.obtainPrizes[0].card.resourceId == 4
            && separate.obtainPrizes[0].gachaLotteryType == "fixed_gacha_bonus" && separate.obtainPrizes[^1].card.resourceId == 3
            && separate.obtainPrizes[^1].gachaLotteryType == "normal" && random.Calls == 10,
            "保底卡不代替十连三星保障，其余九抽仍单独补末位，奖品总数保持请求抽数");
        Reset(149.5f);
        var cycles = Draw(4, count: 401, card: 4);
        Check.That(cycles.obtainPrizes.Take(5).Select(p => p.card.resourceId).SequenceEqual(new[] { 4, 4, 5, 6, 4 })
            && cycles.obtainPrizes.Take(5).All(p => p.gachaLotteryType == "fixed_gacha_bonus")
            && Point() == 150 && Material(15) == 50, "跨200点循环按门槛顺序结算，同次后续未持有保底排除先前保底卡");
        foreach (var pick in new[] { 0L, 1L, 3L })
        {
            Reset(9.5f, 8);
            random.Pick = pick;
            var item = Draw(id: 8).obtainGachaBonusItems.Single();
            Check.That(item.resourceId == (pick == 0 ? 15 : 166), "随机道具按组内权重边界抽取整箱，零权重项不被抽中");
        }
        Reset(19.5f, 8);
        Rejected(() => Draw(id: 8), 409);
        Rejected(() => Draw(id: 8, item: 1), 400);
        Rejected(() => Draw(id: 8, item: 14), 400);
        var chosenItem = Draw(id: 8, item: 13);
        Check.That(chosenItem.obtainGachaBonusItems.Length == 2 && Material(15) == 3 && Material(16) == 2,
            "selectedItemId为选择项ID而非箱ID，零随机权重项仍可主动选择并发全箱");
        foreach (var id in new[] { 4, 5, 6, 9, 10, 12, 13 })
        {
            Reset(9.5f, id);
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<InvalidOperationException>(() => Draw(id: id), "缺组、重复门槛、缺箱、未知类型、双保底及坏随机权重拒绝");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "坏配置不提交消耗、点数或部分奖励");
        }
        Reset(9.5f, 7);
        var partialBefore = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<InvalidOperationException>(() => Draw(4, 7, 100), "先发道具后缺保底供给组");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(partialBefore), "后续保底失败回滚先前道具与全部票券");
        Reset(9.5f, 11);
        var overflow = store.Read(1)!;
        overflow.Data.userMaterials = [new() { materialId = 16, quantity = int.MaxValue }];
        store.Save(1, overflow);
        var overflowBefore = DumpSerializer.Serialize(overflow.Data);
        Check.Throws<OverflowException>(() => Draw(id: 11), "整箱第二项库存溢出");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(overflowBefore), "整箱后项溢出回滚首项、费用和点数");
        foreach (var point in new[] { -1f, 200f, float.NaN })
        {
            Reset(point);
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<InvalidOperationException>(() => Draw(), "无效保底进度拒绝");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "坏进度原样保留，未扣费");
        }
        Reset(9.5f);
        overflow = store.Read(1)!;
        overflow.Data.userGachaBonusPoints.Single().totalGachaBonusPoint = float.MaxValue;
        store.Save(1, overflow);
        overflowBefore = DumpSerializer.Serialize(overflow.Data);
        Check.Throws<OverflowException>(() => Draw(), "累计浮点已不能增加半点时拒绝静默发奖");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(overflowBefore), "累计精度溢出回滚");
        Reset(9.5f);
        var encodingBefore = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        { gacha.ExecuteGacha(1, 1, false); return new GachaExtraChecks.BrokenResponse(); }), "保底发道具和卡牌后编码失败");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(encodingBefore), "编码失败回滚整次抽取及点数");
        Reset(149.5f);
        var saved = store.Read(1)!;
        var database = Path.Combine(directory, "item-receivable.db");
        using (var sqlite = new SqliteUserStore(database)) sqlite.Save(1, saved);
        using var reopened = new SqliteUserStore(database);
        var restored = JsonSerializer.SerializeToNode(reopened.Read(1)!.Data, DumpJson.Options)!.AsObject();
        var expected = JsonSerializer.SerializeToNode(saved.Data, DumpJson.Options)!.AsObject();
        foreach (var key in new[] { "now", "refreshableTypes" }) { restored.Remove(key); expected.Remove(key); }
        Check.That(JsonNode.DeepEquals(restored, expected), "SQLite重启保留200点循环内的149.5点及全部持久状态");
        using var resumed = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<IUserStore>(reopened)
            .AddSingleton<Random>(random).AddSingleton<TimeProvider>(new Clock()).BuildServiceProvider();
        using var resumedScope = resumed.CreateScope();
        var resumedResult = DumpSerializer.Deserialize<UserGachaResponse>(resumedScope.ServiceProvider.GetRequiredService<UserOperation>()
            .Execute(1, () => resumedScope.ServiceProvider.GetRequiredService<GachaService>().ExecuteGacha(1, 1, false, selectedCardId: 5)));
        Check.That(resumedResult.obtainPrizes.Single().card.resourceId == 5 && reopened.Read(1)!.Data.userGachaBonusPoints.Single().gachaBonusPoint == 150,
            "数据库重启后继续跨150点并实际发所选卡");
        Reset(9.5f);
        Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            using var racing = provider.CreateScope();
            racing.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () =>
                racing.ServiceProvider.GetRequiredService<GachaService>().ExecuteGacha(1, 1, false));
        }))).GetAwaiter().GetResult();
        Check.That(Point() == 10.5f && Material(15) == 50 && store.Read(1)!.Data.userChargedCurrency.free == 9400,
            "并发跨同一门槛只发一次道具，两个合法单抽各扣一次费用并累计半点");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "保底计点与发奖不修改其他账号");
    }

    private sealed class DrawRandom : Random
    {
        public Queue<double> Values = new();
        public long Pick;
        public int Calls;
        public override double NextDouble() { Calls++; return Values.Count == 0 ? 0 : Values.Dequeue(); }
        public override long NextInt64(long maxValue) => maxValue == 1 ? 0 : Pick;
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(1000);
    }
}
