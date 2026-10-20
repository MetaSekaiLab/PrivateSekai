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

internal static class RateChoiceGachaChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "rate-choice-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string json) => File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        var pools = JsonNode.Parse("""
            [{"id":1,"startAt":1,"wishSelectCount":4,"rateChoiceGachaWishGroupId":1,"gachaCeilItemId":9,
              "gachaDetails":[{"id":102,"cardId":2,"weight":1},{"id":103,"cardId":3,"weight":1},
               {"id":104,"cardId":4,"weight":999,"isWish":true},{"id":105,"cardId":5,"weight":1,"isWish":true},
               {"id":106,"cardId":6,"weight":0,"isWish":true},{"id":107,"cardId":7,"weight":222,"isWish":true},
               {"id":108,"cardId":8,"weight":1,"isWish":true},{"id":109,"cardId":9,"weight":3}],
              "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":50},
               {"cardRarityType":"rarity_3","lotteryType":"normal","rate":20},{"cardRarityType":"rarity_4","lotteryType":"normal","rate":10},
               {"cardRarityType":"rarity_4","lotteryType":"rate_choice_first","rate":12},{"cardRarityType":"rarity_4","lotteryType":"rate_choice_second","rate":8}],
              "gachaBehaviors":[{"id":1,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300},
               {"id":2,"spinCount":10,"gachaBehaviorType":"over_rarity_3_once","costResourceType":"jewel","costResourceQuantity":3000},
               {"id":3,"spinCount":10,"executeLimit":1,"gachaBehaviorType":"over_rarity_3_once","costResourceType":"paid_jewel","costResourceQuantity":1500},
               {"id":4,"spinCount":1,"costResourceType":"gacha_ticket","costResourceId":1,"costResourceQuantity":1}]}]
            """)!.AsArray();
        foreach (var id in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 })
        {
            var pool = pools[0]!.DeepClone();
            pool["id"] = id;
            if (id == 2) foreach (var detail in pool["gachaDetails"]!.AsArray()) detail!["id"] = detail["id"]!.GetValue<int>() + 1000;
            if (id == 3) pool["rateChoiceGachaWishGroupId"] = 0;
            if (id == 4) pool["endAt"] = 1000;
            if (id == 5) pool["startAt"] = 1001;
            if (id == 6) pool["rateChoiceGachaWishGroupId"] = 999;
            if (id == 7)
            {
                pool["gachaDetails"]!.AsArray().RemoveAt(7);
                pool["gachaDetails"]!.AsArray().RemoveAt(6);
            }
            if (id == 8) pool["gachaCardRarityRates"]!.AsArray().RemoveAt(4);
            if (id == 9) pool["gachaCardRarityRates"]![0]!["rate"] = -1;
            if (id == 10)
            {
                pool["gachaCardRarityRates"]![0]!["rate"] = 3e38;
                pool["gachaCardRarityRates"]![1]!["rate"] = 3e38;
            }
            pools.Add(pool);
        }
        Table("gachas", pools.ToJsonString());
        Table("cards", """
            [{"id":2,"characterId":1,"cardRarityType":"rarity_2"},{"id":3,"characterId":1,"cardRarityType":"rarity_3"},
             {"id":4,"characterId":1,"cardRarityType":"rarity_4"},{"id":5,"characterId":1,"cardRarityType":"rarity_4"},
             {"id":6,"characterId":1,"cardRarityType":"rarity_4"},{"id":7,"characterId":1,"cardRarityType":"rarity_4"},
             {"id":8,"characterId":1,"cardRarityType":"rarity_4"},{"id":9,"characterId":1,"cardRarityType":"rarity_4"}]
            """);
        Table("rateChoiceGachaWishes", """[{"id":1,"groupId":1,"seq":1,"lotteryType":"rate_choice_first","selectCount":2},{"id":2,"groupId":1,"seq":2,"lotteryType":"rate_choice_second","selectCount":2}]""");
        Table("characterMissionV2s", """[{"id":1,"characterId":1,"characterMissionType":"collect_member","parameterGroupId":1},{"id":2,"characterId":1,"characterMissionType":"waiting_room","parameterGroupId":1}]""");
        Table("characterMissionV2ParameterGroups", """[{"id":1,"seq":1,"requirement":1}]""");
        foreach (var table in new[] { "cardEpisodes", "cardExtras", "cardCostume3ds", "costume3dShopItems", "releaseConditions", "characterArchiveVoices", "beginnerMissionV2s", "honorMissions" }) Table(table, "[]");
        var store = new MemoryUserStore();
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        var random = new DrawRandom();
        var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton(master)
            .AddSingleton<IUserStore>(store).AddSingleton<Random>(random).AddSingleton<TimeProvider>(new Clock()).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        void Reset()
        {
            var state = TestUsers.Create(1);
            state.Data.userChargedCurrency = new() { free = 10000, paid = 10000 };
            state.Data.userGachaTickets = [new() { gachaTicketId = 1, quantity = 10 }];
            store.Save(1, state);
            random.Values = new();
            random.Pick = 0;
            random.Calls = 0;
        }
        UserRateChoiceGachaWishRequest Request(int pool = 1) => new()
        {
            gachaId = pool, rateChoiceGachaDetails = new[] { (1, 104), (1, 105), (2, 106), (2, 107) }
                .Select(d => new UserRateChoiceGachaWishRequest.RateChoiceGachaDetail
                    { rateChoiceGachaWishId = d.Item1, gachaDetailId = d.Item2 + (pool == 2 ? 1000 : 0) }).ToList()
        };
        void Select(UserRateChoiceGachaWishRequest? request = null) => operation.Execute(1, () =>
        {
            gacha.SaveRateChoiceGachaWish(request ?? Request());
            return new UserRateChoiceGachaWishResponse { updatedResources = user.BuildRefresh() };
        });
        UserGachaResponse Draw(int behavior = 1, int pool = 1, int? count = null) => DumpSerializer.Deserialize<UserGachaResponse>(operation.Execute(1, () =>
        {
            var result = gacha.ExecuteGacha(pool, behavior, false, count);
            result.updatedResources = user.BuildRefresh(prepareRefresh: false);
            return result;
        }));
        void Rejected(Action action, int status)
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            var error = Check.Throws<GachaRequestRejectedException>(action, "非法Rate Choice请求拒绝");
            Check.That(error.Status == status && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "拒绝保存或抽取时原选择、费用、卡牌及全部用户状态不变");
        }
        Reset();
        Rejected(() => Draw(), 409);
        foreach (var kind in new[] { "null", "null-entry", "empty", "partial", "duplicate", "unknown-group", "wrong-count", "card-id", "not-wish", "wrong-rarity", "other-pool" })
        {
            var request = Request();
            if (kind == "null") request.rateChoiceGachaDetails = null!;
            if (kind == "null-entry") request.rateChoiceGachaDetails[0] = null!;
            if (kind == "empty") request.rateChoiceGachaDetails.Clear();
            if (kind == "partial") request.rateChoiceGachaDetails.RemoveAt(0);
            if (kind == "duplicate") request.rateChoiceGachaDetails[2].gachaDetailId = 104;
            if (kind == "unknown-group") request.rateChoiceGachaDetails[0].rateChoiceGachaWishId = 999;
            if (kind == "wrong-count") request.rateChoiceGachaDetails[0].rateChoiceGachaWishId = 2;
            if (kind == "card-id") request.rateChoiceGachaDetails[0].gachaDetailId = 4;
            if (kind == "not-wish") request.rateChoiceGachaDetails[0].gachaDetailId = 109;
            if (kind == "wrong-rarity") request.rateChoiceGachaDetails[0].gachaDetailId = 103;
            if (kind == "other-pool") request.rateChoiceGachaDetails[0].gachaDetailId = 1104;
            Rejected(() => Select(request), 400);
        }
        Rejected(() => Select(Request(3)), 400);
        Rejected(() => Select(Request(4)), 409);
        Rejected(() => Select(Request(5)), 409);
        Rejected(() => Select(Request(999)), 404);
        foreach (var pool in new[] { 6, 7, 8, 9, 10 })
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<InvalidOperationException>(() => Select(Request(pool)), "缺愿望组、空普通池、缺概率档、负概率或概率总和溢出不能保存选择");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "无效master不创建伪造或残缺选择");
        }
        Select();
        Check.That(store.Read(1)!.Data.userRateChoiceGachaWishes.Length == 4 && store.Read(1)!.Data.userCards.Length == 0
            && store.Read(1)!.Data.userChargedCurrency.free == 10000, "完整分组只保存一次选择，不抽卡或收费");
        Rejected(() => Select(), 409);
        Select(Request(2));
        Check.That(store.Read(1)!.Data.userRateChoiceGachaWishes.Length == 8, "相同wish组的不同卡池独立注册，不覆盖已有选择");
        foreach (var item in new[] { (0d, 0L, 2), (.5, 0L, 3), (.7, 0L, 8),
            (.7, 3L, 9), (.8, 0L, 4), (.8, 1L, 5), (.92, 0L, 6), (.999, 1L, 7) })
        {
            Reset();
            Select();
            random.Values = new([item.Item1]);
            random.Pick = item.Item2;
            var result = Draw();
            Check.That(result.obtainPrizes.Single().card.resourceId == item.Item3 && result.obtainPrizes.Single().gachaLotteryType == "normal",
                "按完整概率段选择rarity与lottery，普通四星排除已选，主副档各自等权，响应保持普通奖品类型");
        }
        Reset();
        Select();
        random.Values = new(Enumerable.Repeat(0d, 10).Append(.7));
        var ten = Draw(2);
        Check.That(ten.obtainPrizes.Length == 10 && ten.obtainPrizes.Take(9).All(p => p.card.resourceId == 2)
            && ten.obtainPrizes[^1].card.resourceId == 4 && ten.obtainPrizes[^1].gachaLotteryType == "normal" && random.Calls == 11,
            "十连不足三星时仅重抽末位，条件概率保留主副档，不退回仅normal池");
        Check.That(store.Read(1)!.Data.userCards.Single(c => c.cardId == 2).duplicateCount == 8
            && store.Read(1)!.Data.userCharacterMissions.Single(m => m.characterMissionType == "collect_member").progress == 2
            && store.Read(1)!.Data.userCharacterMissions.Single(m => m.characterMissionType == "waiting_room").progress == 8
            && store.Read(1)!.Data.userGachaCeilItems.Single().quantity == 10 && store.Read(1)!.Data.userGachas.Single().count == 1,
            "Rate Choice沿用新卡、重复卡、任务、十连计次和天井结算");
        Reset();
        Select();
        random.Values = new(new[] { .8 }.Concat(Enumerable.Repeat(0d, 9)));
        Draw(3);
        Check.That(random.Calls == 10 && store.Read(1)!.Data.userChargedCurrency.paid == 8500, "已有主档四星时不再次补保底，付费十连扣一次配置费用");
        Rejected(() => Draw(3), 409);
        Reset();
        Select();
        random.Values = new([.8, .92, .7]);
        var multiple = Draw(4, count: 3);
        Check.That(multiple.obtainPrizes.Select(p => p.card.resourceId).SequenceEqual(new[] { 4, 6, 8 })
            && store.Read(1)!.Data.userGachaTickets.Single().quantity == 7 && store.Read(1)!.Data.userGachas.Single().count == 3,
            "指定多次票券单抽逐次使用已注册主副名单，费用和次数按实际抽数累计");
        foreach (var kind in new[] { "missing-detail", "partial" })
        {
            Reset();
            Select();
            var state = store.Read(1)!;
            if (kind == "partial") state.Data.userRateChoiceGachaWishes = state.Data.userRateChoiceGachaWishes.Take(3).ToArray();
            else state.Data.userRateChoiceGachaWishes[0].gachaDetailId = 999;
            store.Save(1, state);
            var before = DumpSerializer.Serialize(state.Data);
            Check.Throws<InvalidOperationException>(() => Draw(), "不完整或失效的已存名单不能静默恢复normal概率");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "失效选择拒绝保留全部原状态");
        }
        foreach (var draw in new[] { false, true })
        {
            Reset();
            if (draw) Select();
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
            {
                if (draw) gacha.ExecuteGacha(1, 1, false);
                else gacha.SaveRateChoiceGachaWish(Request());
                return new GachaExtraChecks.BrokenResponse();
            }), "选择注册或实际抽取后响应编码失败");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败整体回滚选择或抽卡的费用、卡牌和状态");
        }
        Reset();
        Select();
        var overflow = store.Read(1)!;
        overflow.Data.userGachas = [new() { gachaId = 1, gachaBehaviorId = 1, count = int.MaxValue }];
        store.Save(1, overflow);
        var overflowBefore = DumpSerializer.Serialize(overflow.Data);
        Check.Throws<OverflowException>(() => Draw(), "发卡后行为计数溢出拒绝提交");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(overflowBefore), "溢出回滚已扣费用、新卡和任务");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "选择注册和实际抽取不修改其他账号");
        Reset();
        var race = Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            using var racing = provider.CreateScope();
            try
            {
                racing.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () =>
                {
                    racing.ServiceProvider.GetRequiredService<GachaService>().SaveRateChoiceGachaWish(Request());
                    return null;
                });
                return true;
            }
            catch (GachaRequestRejectedException error) when (error.Status == 409) { return false; }
        }))).GetAwaiter().GetResult();
        Check.That(race.Count(success => success) == 1 && store.Read(1)!.Data.userRateChoiceGachaWishes.Length == 4,
            "并发注册仅一次成功，不重复追加或覆盖已锁定选择");
        random.Values = new([.8]);
        Draw();
        var saved = store.Read(1)!;
        var database = Path.Combine(directory, "rate-choice.db");
        using (var sqlite = new SqliteUserStore(database)) sqlite.Save(1, saved);
        using var reopened = new SqliteUserStore(database);
        var restored = JsonSerializer.SerializeToNode(reopened.Read(1)!.Data, DumpJson.Options)!.AsObject();
        var expected = JsonSerializer.SerializeToNode(saved.Data, DumpJson.Options)!.AsObject();
        foreach (var key in new[] { "now", "refreshableTypes" }) { restored.Remove(key); expected.Remove(key); }
        Check.That(JsonNode.DeepEquals(restored, expected), "SQLite重启保留名单和所有抽卡相关持久字段");
        using var resumed = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<IUserStore>(reopened)
            .AddSingleton<Random>(random).AddSingleton<TimeProvider>(new Clock()).BuildServiceProvider();
        using var resumedScope = resumed.CreateScope();
        random.Values = new([.92]);
        var resumedResponse = DumpSerializer.Deserialize<UserGachaResponse>(resumedScope.ServiceProvider.GetRequiredService<UserOperation>()
            .Execute(1, () => resumedScope.ServiceProvider.GetRequiredService<GachaService>().ExecuteGacha(1, 1, false)));
        Check.That(resumedResponse.obtainPrizes.Single().card.resourceId == 6, "重启后实际抽取继续使用持久化的副档名单");
    }

    private sealed class DrawRandom : Random
    {
        public Queue<double> Values { get; set; } = new();
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
