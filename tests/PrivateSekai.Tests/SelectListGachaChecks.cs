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

internal static class SelectListGachaChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "select-list-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string json) => File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        var pool = JsonNode.Parse("""
            {"id":1,"startAt":1,"wishSelectCount":2,"gachaCeilItemId":9,
             "gachaDetails":[{"id":102,"cardId":2,"weight":1},{"id":103,"cardId":3,"weight":1}],
             "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":50},
              {"cardRarityType":"rarity_3","lotteryType":"normal","rate":30},{"cardRarityType":"rarity_4","lotteryType":"normal","rate":20}],
             "gachaBehaviors":[{"id":1,"spinCount":10,"executeLimit":1,"gachaBehaviorType":"over_rarity_4_once","costResourceType":"paid_jewel","costResourceQuantity":3000},
              {"id":2,"spinCount":10,"executeLimit":3,"gachaBehaviorType":"over_rarity_3_once","costResourceType":"jewel","costResourceQuantity":3000},
              {"id":3,"spinCount":1,"costResourceType":"gacha_ticket","costResourceId":1,"costResourceQuantity":1}]}
            """)!;
        var cardRows = new JsonArray
        {
            new JsonObject { ["id"] = 2, ["characterId"] = 1, ["cardRarityType"] = "rarity_2" },
            new JsonObject { ["id"] = 3, ["characterId"] = 1, ["cardRarityType"] = "rarity_3" }
        };
        for (var id = 4; id <= 14; id++)
        {
            pool["gachaDetails"]!.AsArray().Add(new JsonObject
                { ["id"] = 100 + id, ["cardId"] = id, ["weight"] = id == 4 ? 999 : id == 5 ? 0 : 1, ["isWish"] = id < 14 });
            cardRows.Add(new JsonObject { ["id"] = id, ["characterId"] = 1, ["cardRarityType"] = "rarity_4" });
        }
        var pools = new JsonArray(pool);
        foreach (var id in Enumerable.Range(2, 12))
        {
            var clone = pool.DeepClone();
            clone["id"] = id;
            if (id == 2) foreach (var detail in clone["gachaDetails"]!.AsArray()) detail!["id"] = detail["id"]!.GetValue<int>() + 1000;
            if (id == 3) clone["wishSelectCount"] = 0;
            if (id == 4) clone["rateChoiceGachaWishGroupId"] = 1;
            if (id == 5) clone["wishFixedSelectCount"] = 1;
            if (id == 6) clone["endAt"] = 1000;
            if (id == 7) clone["startAt"] = 1001;
            if (id == 8) clone["spinLimit"] = 20;
            if (id == 9) clone["gachaCardRarityRates"]![0]!["rate"] = -1;
            if (id == 10) clone["gachaDetails"]!.AsArray().RemoveAt(1);
            if (id == 11)
            {
                clone["gachaCardRarityRates"]![0]!["rate"] = 3e38;
                clone["gachaCardRarityRates"]![1]!["rate"] = 3e38;
            }
            if (id == 12)
            {
                clone["wishSelectCount"] = 3;
                clone["gachaBehaviors"] = new JsonArray(pool["gachaBehaviors"]![1]!.DeepClone());
            }
            if (id == 13)
            {
                clone["wishSelectCount"] = 10;
                clone["gachaBehaviors"] = new JsonArray(pool["gachaBehaviors"]![0]!.DeepClone());
            }
            pools.Add(clone);
        }
        Table("gachas", pools.ToJsonString());
        Table("cards", cardRows.ToJsonString());
        Table("characterMissionV2s", """[{"id":1,"characterId":1,"characterMissionType":"collect_member","parameterGroupId":1},{"id":2,"characterId":1,"characterMissionType":"waiting_room","parameterGroupId":1}]""");
        Table("characterMissionV2ParameterGroups", """[{"id":1,"seq":1,"requirement":1}]""");
        foreach (var table in new[] { "cardEpisodes", "cardCostume3ds", "costume3dShopItems", "releaseConditions", "characterArchiveVoices", "beginnerMissionV2s", "honorMissions" }) Table(table, "[]");
        var store = new MemoryUserStore();
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        var random = new DrawRandom();
        var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<IUserStore>(store)
            .AddSingleton<Random>(random).AddSingleton<TimeProvider>(new Clock()).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        void Reset()
        {
            var state = TestUsers.Create(1);
            state.Data.userChargedCurrency = new() { free = 20000, paid = 10000 };
            state.Data.userGachaTickets = [new() { gachaTicketId = 1, quantity = 10 }];
            store.Save(1, state);
            random.Values = new();
            random.Pick = 0;
            random.Calls = 0;
        }
        void Select(int id = 1, int[]? ids = null) => operation.Execute(1, () =>
        {
            gacha.SaveGachaWish(new() { gachaId = id, gachaDetailIds = ids ?? [104, 105] });
            return new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() };
        });
        UserGachaResponse Draw(int behavior = 3, int id = 1, int? count = null) => DumpSerializer.Deserialize<UserGachaResponse>(operation.Execute(1, () =>
        {
            var result = gacha.ExecuteGacha(id, behavior, false, count);
            result.updatedResources = user.BuildRefresh(prepareRefresh: false);
            return result;
        }));
        void Rejected(Action action, int status)
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            var error = Check.Throws<GachaRequestRejectedException>(action, "非法Select List请求拒绝");
            Check.That(error.Status == status && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "拒绝时名单、费用、卡牌和所有状态不变");
        }
        Reset();
        Rejected(() => Draw(), 409);
        foreach (var ids in new[] { Array.Empty<int>(), new[] { 104 }, new[] { 104, 104 }, new[] { 4, 5 },
            new[] { 104, 114 }, new[] { 104, 103 }, new[] { 104, 1105 }, new[] { 104, -1 }, new[] { 104, 105, 106 } })
            Rejected(() => Select(ids: ids), 400);
        Rejected(() => operation.Execute(1, () => { gacha.SaveGachaWish(new() { gachaId = 1, gachaDetailIds = null! }); return null; }), 400);
        foreach (var id in new[] { 3, 4, 5 }) Rejected(() => Select(id), 400);
        foreach (var id in new[] { 6, 7 }) Rejected(() => Select(id), 409);
        Rejected(() => Select(999), 404);
        foreach (var id in new[] { 9, 10, 11 })
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<InvalidOperationException>(() => Select(id), "负概率、空正概率段及概率总和溢出拒绝");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "坏master不保存残缺名单");
        }
        Select(ids: [105, 104]);
        Select(2, [1104, 1105]);
        Select();
        Check.That(store.Read(1)!.Data.userGachaWishes.Select(w => w.gachaDetailId).SequenceEqual(new[] { 104, 105, 1104, 1105 })
            && store.Read(1)!.Data.userCards.Length == 0 && store.Read(1)!.Data.userChargedCurrency.free == 20000,
            "重复保存替换本池完整名单，保留其他池且不收费或发卡");
        foreach (var item in new[] { (0d, 0L, 2), (.5, 0L, 3), (.8, 0L, 4), (.999, 1L, 5) })
        {
            random.Values = new([item.Item1]);
            random.Pick = item.Item2;
            Check.That(Draw().obtainPrizes.Single().card.resourceId == item.Item3,
                "低稀有度保持普通池，四星只在所选详情中等权，不使用原始权重或未选四星");
        }
        Reset();
        Select();
        random.Values = new(Enumerable.Repeat(0d, 10).Append(0d));
        var four = Draw(1);
        Check.That(four.obtainPrizes.Take(9).All(p => p.card.resourceId == 2) && four.obtainPrizes[^1].card.resourceId == 4
            && four.obtainPrizes.All(p => p.gachaLotteryType == "normal") && random.Calls == 11, "付费十连不足四星时仅末位重抽所选四星，响应保持normal");
        var result = store.Read(1)!.Data;
        Check.That(result.userChargedCurrency.paid == 7000 && result.userGachas.Single().count == 1 && result.userGachaCeilItems.Single().quantity == 10
            && result.userCards.Single(c => c.cardId == 2).duplicateCount == 8 && result.userCharacterMissions.Single(m => m.characterMissionType == "collect_member").progress == 2
            && result.userCharacterMissions.Single(m => m.characterMissionType == "waiting_room").progress == 8, "选择型十连完整结算费用、次数、天井、重复卡及任务");
        Rejected(() => Draw(1), 409);
        Reset();
        Select();
        random.Values = new(new[] { .8 }.Concat(Enumerable.Repeat(0d, 9)));
        Draw(1);
        Check.That(random.Calls == 10, "十连已有所选四星时不补抽");
        Reset();
        Select(12, [104, 105, 106]);
        random.Values = new(Enumerable.Repeat(0d, 11));
        var three = Draw(2, 12);
        Check.That(three.obtainPrizes[^1].card.resourceId == 3 && random.Calls == 11, "三星保底池不因Wish而变成四星必出，按条件概率重抽末位");
        Select(12, [107, 108, 109]);
        random.Values = new(Enumerable.Repeat(.9, 20));
        Draw(2, 12);
        var changed = Draw(2, 12);
        Check.That(changed.obtainPrizes.All(p => p.card.resourceId == 7) && store.Read(1)!.Data.userChargedCurrency.free == 11000,
            "已抽后仍可改选，后两次实际使用新名单，三星池不继承旧四星候选");
        Rejected(() => Select(12, [104, 105, 106]), 409);
        Rejected(() => Draw(2, 12), 409);
        Reset();
        Select(13, Enumerable.Range(104, 10).ToArray());
        Draw(1, 13);
        Rejected(() => Select(13, Enumerable.Range(104, 10).ToArray()), 409);
        Reset();
        Select(8);
        Draw(2, 8);
        Select(8, [106, 107]);
        Draw(2, 8);
        Rejected(() => Select(8), 409);
        foreach (var kind in new[] { "partial", "duplicate", "stale" })
        {
            Reset();
            Select();
            var state = store.Read(1)!;
            if (kind == "partial") state.Data.userGachaWishes = state.Data.userGachaWishes.Take(1).ToArray();
            else state.Data.userGachaWishes[1].gachaDetailId = kind == "duplicate" ? 104 : 999;
            store.Save(1, state);
            var before = DumpSerializer.Serialize(state.Data);
            Check.Throws<InvalidOperationException>(() => Draw(), "失效已存名单不能静默使用全池");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "坏名单抽取整体回滚");
        }
        foreach (var draw in new[] { false, true })
        {
            Reset();
            Select();
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
            {
                if (draw) gacha.ExecuteGacha(1, 1, false);
                else gacha.SaveGachaWish(new() { gachaId = 1, gachaDetailIds = [106, 107] });
                return new GachaExtraChecks.BrokenResponse();
            }), "改选或完整发奖后响应编码失败");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败回滚原名单或全部费用、奖励和次数");
        }
        Reset();
        Select();
        random.Values = new([.8, .8, .8]);
        Draw(count: 3);
        Check.That(store.Read(1)!.Data.userGachaTickets.Single().quantity == 7 && store.Read(1)!.Data.userGachas.Single().count == 3,
            "指定多次票券抽取仍受Wish名单限制且逐次累计费用与次数");
        var saved = store.Read(1)!;
        var database = Path.Combine(directory, "select-list.db");
        using (var sqlite = new SqliteUserStore(database)) sqlite.Save(1, saved);
        using var reopened = new SqliteUserStore(database);
        var restored = JsonSerializer.SerializeToNode(reopened.Read(1)!.Data, DumpJson.Options)!.AsObject();
        var expected = JsonSerializer.SerializeToNode(saved.Data, DumpJson.Options)!.AsObject();
        foreach (var key in new[] { "now", "refreshableTypes" }) { restored.Remove(key); expected.Remove(key); }
        Check.That(JsonNode.DeepEquals(restored, expected), "SQLite重启保留选择、费用、卡牌和全部持久字段");
        using var resumed = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<IUserStore>(reopened)
            .AddSingleton<Random>(random).AddSingleton<TimeProvider>(new Clock()).BuildServiceProvider();
        using var resumedScope = resumed.CreateScope();
        random.Values = new([.8]);
        random.Pick = 1;
        var resumedResult = DumpSerializer.Deserialize<UserGachaResponse>(resumedScope.ServiceProvider.GetRequiredService<UserOperation>()
            .Execute(1, () => resumedScope.ServiceProvider.GetRequiredService<GachaService>().ExecuteGacha(1, 3, false)));
        Check.That(resumedResult.obtainPrizes.Single().card.resourceId == 5, "数据库重启后实际抽中原名单第二位，零原始权重不影响等权选择");
        Reset();
        Select(13, Enumerable.Range(104, 10).ToArray());
        var races = Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            using var racing = provider.CreateScope();
            try
            {
                racing.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () =>
                    racing.ServiceProvider.GetRequiredService<GachaService>().ExecuteGacha(13, 1, false));
                return true;
            }
            catch (GachaRequestRejectedException error) when (error.Status == 409) { return false; }
        }))).GetAwaiter().GetResult();
        Check.That(races.Count(success => success) == 1 && store.Read(1)!.Data.userChargedCurrency.paid == 7000
            && store.Read(1)!.Data.userGachas.Single().count == 1, "最后一次并发十连只扣费和结算一次");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "本功能不修改其他账号");
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
