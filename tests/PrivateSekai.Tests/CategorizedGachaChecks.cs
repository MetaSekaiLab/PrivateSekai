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

internal static class CategorizedGachaChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "categorized-gacha-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string json) => File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        var pool = JsonNode.Parse("""
            {"id":1,"startAt":1,"wishSelectCount":5,"wishFixedSelectCount":2,"wishLimitedSelectCount":1,"gachaCeilItemId":9,
             "gachaDetails":[{"id":102,"cardId":2,"weight":1},{"id":103,"cardId":3,"weight":1},
              {"id":104,"cardId":4,"weight":999,"isWish":true,"gachaDetailWishType":"fixed"},
              {"id":105,"cardId":5,"weight":0,"isWish":true,"gachaDetailWishType":"fixed"},
              {"id":106,"cardId":6,"weight":999,"isWish":true,"gachaDetailWishType":"limited"},
              {"id":107,"cardId":7,"weight":1,"isWish":true,"gachaDetailWishType":"limited"},
              {"id":108,"cardId":8,"weight":100,"isWish":true,"gachaDetailWishType":"normal"},
              {"id":109,"cardId":9,"weight":0,"isWish":true,"gachaDetailWishType":"normal"},
              {"id":110,"cardId":10,"weight":3,"isWish":true,"gachaDetailWishType":"normal"},
              {"id":111,"cardId":11,"weight":1,"isWish":true,"gachaDetailWishType":"normal"},
              {"id":112,"cardId":12,"weight":4},{"id":113,"cardId":13,"weight":1,"isWish":true,"gachaDetailWishType":"normal"}],
             "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":50},
              {"cardRarityType":"rarity_3","lotteryType":"normal","rate":30},{"cardRarityType":"rarity_4","lotteryType":"normal","rate":10},
              {"cardRarityType":"rarity_4","lotteryType":"categorized_wish","rate":10}],
             "gachaBehaviors":[{"id":1,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300},
              {"id":2,"spinCount":10,"executeLimit":1,"gachaBehaviorType":"over_rarity_3_once","costResourceType":"jewel","costResourceQuantity":3000},
              {"id":3,"spinCount":10,"executeLimit":1,"gachaBehaviorType":"over_rarity_3_once","costResourceType":"paid_jewel","costResourceQuantity":1500},
              {"id":4,"spinCount":1,"costResourceType":"gacha_ticket","costResourceId":1,"costResourceQuantity":1}]}
            """)!;
        var pools = new JsonArray(pool);
        foreach (var id in Enumerable.Range(2, 16))
        {
            var clone = pool.DeepClone();
            clone["id"] = id;
            if (id == 2) foreach (var detail in clone["gachaDetails"]!.AsArray()) detail!["id"] = detail["id"]!.GetValue<int>() + 1000;
            if (id == 3) clone["wishSelectCount"] = 3;
            if (id == 4) { clone["wishFixedSelectCount"] = 0; clone["wishLimitedSelectCount"] = 0; }
            if (id == 5) clone["rateChoiceGachaWishGroupId"] = 1;
            if (id == 6) clone["endAt"] = 1000;
            if (id == 7) clone["startAt"] = 1001;
            if (id == 8) clone["wishSelectCount"] = 2;
            if (id == 9) clone["wishFixedSelectCount"] = 3;
            if (id == 10) clone["gachaCardRarityRates"]![0]!["rate"] = -1;
            if (id == 11)
            {
                clone["gachaCardRarityRates"]![0]!["rate"] = 3e38;
                clone["gachaCardRarityRates"]![1]!["rate"] = 3e38;
            }
            if (id == 12)
                foreach (var detail in clone["gachaDetails"]!.AsArray().Where(d => d!["id"]!.GetValue<int>() is 107 or 110 or 111 or 112).ToArray())
                    clone["gachaDetails"]!.AsArray().Remove(detail);
            if (id == 13) clone["gachaCardRarityRates"]![3]!["lotteryType"] = "other";
            if (id == 14) clone["wishLimitedSelectCount"] = -1;
            if (id >= 15)
            {
                var fixedCount = id == 16 ? 5 : 6;
                clone["wishSelectCount"] = id == 17 ? 10 : 8;
                clone["wishFixedSelectCount"] = fixedCount;
                clone["wishLimitedSelectCount"] = id == 17 ? 1 : 8 - fixedCount;
                var details = new JsonArray(pool["gachaDetails"]![0]!.DeepClone(), pool["gachaDetails"]![1]!.DeepClone());
                for (var card = 4; card <= 28; card++) details.Add(new JsonObject
                {
                    ["id"] = 100 + card, ["cardId"] = card, ["weight"] = 1, ["isWish"] = true,
                    ["gachaDetailWishType"] = card < 4 + fixedCount ? "fixed" : card < 9 + fixedCount ? "limited" : "normal"
                });
                clone["gachaDetails"] = details;
            }
            pools.Add(clone);
        }
        Table("gachas", pools.ToJsonString());
        Table("cards", JsonSerializer.Serialize(Enumerable.Range(2, 27).Select(id => new
            { id, characterId = 1, cardRarityType = id is 2 or 13 ? "rarity_2" : id == 3 ? "rarity_3" : "rarity_4" })));
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
        void Reset()
        {
            var state = TestUsers.Create(1);
            state.Data.userChargedCurrency = new() { free = 10000, paid = 10000 };
            state.Data.userGachaTickets = [new() { gachaTicketId = 1, quantity = 10 }];
            store.Save(1, state);
            random.Values.Clear();
            random.Pick = 0;
            random.Calls = 0;
        }
        UserCategorizedGachaWishRequest Request(int id = 1) => new()
        {
            gachaId = id, gachaDetailFixedIds = id == 2 ? [1104, 1105] : [104, 105],
            gachaDetailLimitedIds = id == 2 ? [1106] : [106], gachaDetailNormalIds = id == 2 ? [1108, 1109] : [108, 109]
        };
        void Select(UserCategorizedGachaWishRequest? request = null) => operation.Execute(1, () =>
        {
            gacha.SaveCategorizedGachaWish(request ?? Request());
            return new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() };
        });
        UserGachaResponse Draw(int behavior = 1, int id = 1, int? count = null) => DumpSerializer.Deserialize<UserGachaResponse>(operation.Execute(1, () =>
        {
            var response = gacha.ExecuteGacha(id, behavior, false, count);
            response.updatedResources = user.BuildRefresh(prepareRefresh: false);
            return response;
        }));
        void Rejected(Action action, int status)
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            var error = Check.Throws<GachaRequestRejectedException>(action, "非法分类Wish请求拒绝");
            Check.That(error.Status == status && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "拒绝保留全部名单、费用、奖励和次数");
        }
        Reset();
        Rejected(() => Draw(), 409);
        foreach (var kind in new[] { "null-fixed", "null-special", "null-normal", "partial", "duplicate", "cross-category", "wrong-fixed", "limited-in-normal", "not-wish", "wrong-rarity", "other-pool", "card-id" })
        {
            var request = Request();
            if (kind == "null-fixed") request.gachaDetailFixedIds = null!;
            if (kind == "null-special") request.gachaDetailLimitedIds = null!;
            if (kind == "null-normal") request.gachaDetailNormalIds = null!;
            if (kind == "partial") request.gachaDetailNormalIds = [108];
            if (kind == "duplicate") request.gachaDetailNormalIds = [108, 108];
            if (kind == "cross-category") request.gachaDetailLimitedIds = [108];
            if (kind == "wrong-fixed") request.gachaDetailFixedIds = [104, 107];
            if (kind == "limited-in-normal") request.gachaDetailNormalIds = [107, 108];
            if (kind == "not-wish") request.gachaDetailNormalIds = [108, 112];
            if (kind == "wrong-rarity") request.gachaDetailNormalIds = [108, 113];
            if (kind == "other-pool") request.gachaDetailNormalIds = [108, 1109];
            if (kind == "card-id") request.gachaDetailNormalIds = [8, 9];
            Rejected(() => Select(request), 400);
        }
        foreach (var id in new[] { 4, 5 }) Rejected(() => Select(Request(id)), 400);
        foreach (var id in new[] { 6, 7 }) Rejected(() => Select(Request(id)), 409);
        Rejected(() => Select(Request(999)), 404);
        foreach (var id in new[] { 8, 9, 10, 11, 12, 13, 14 })
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<InvalidOperationException>(() => Select(Request(id)), "人数、固定成员、概率或普通四星池配置不合法");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "坏master不能保存伪造名单");
        }
        Select();
        Select(Request(2));
        Check.That(store.Read(1)!.Data.userCategorizedGachaWishes.Length == 10 && store.Read(1)!.Data.userCards.Length == 0
            && store.Read(1)!.Data.userChargedCurrency.free == 10000, "三类一次完整注册，保存详情与槽位类型，跨池独立且不收费发卡");
        Rejected(() => Select(), 409);
        var replacement = Request();
        replacement.gachaDetailLimitedIds = [107];
        Rejected(() => Select(replacement), 409);
        foreach (var item in new[] { (0d, 0L, 2), (.5, 0L, 3), (.8, 0L, 7), (.8, 1L, 10), (.8, 4L, 11), (.8, 5L, 12),
            (.9, 0L, 4), (.9, 1L, 5), (.9, 2L, 6), (.9, 3L, 8), (.999, 4L, 9) })
        {
            Reset();
            Select();
            random.Values = new([item.Item1]);
            random.Pick = item.Item2;
            var result = Draw();
            Check.That(result.obtainPrizes.Single().card.resourceId == item.Item3 && result.obtainPrizes.Single().gachaLotteryType == "normal",
                "分类Wish档内三类统一等权，普通四星排除所选并保留原权重，响应不伪造lottery取值");
        }
        Reset();
        var specialNormal = Request();
        specialNormal.gachaDetailLimitedIds = [110];
        Select(specialNormal);
        Check.That(store.Read(1)!.Data.userCategorizedGachaWishes.Single(w => w.gachaDetailId == 110).categorizedWishType == "limited",
            "普通成员可放特别名额，持久类型按所占槽位保存而非复制master类别");
        random.Values = new([.95]);
        random.Pick = 4;
        Check.That(Draw().obtainPrizes.Single().card.resourceId == 10, "特别名额的普通成员实际进入统一Wish档");
        Reset();
        var zeroNormal = Request(3);
        zeroNormal.gachaDetailNormalIds = null!;
        Select(zeroNormal);
        Check.That(store.Read(1)!.Data.userCategorizedGachaWishes.Length == 3, "普通配额为零时空数组或null不强制创建额外名额");
        foreach (var id in new[] { 15, 16, 17 })
        {
            Reset();
            var fixedCount = id == 16 ? 5 : 6;
            var specialCount = id == 17 ? 1 : 8 - fixedCount;
            Select(new() { gachaId = id, gachaDetailFixedIds = Enumerable.Range(104, fixedCount).ToArray(),
                gachaDetailLimitedIds = Enumerable.Range(104 + fixedCount, specialCount).ToArray(),
                gachaDetailNormalIds = id == 17 ? [115, 116, 117] : [] });
            random.Values = new([.95]);
            Draw(id: id);
            Check.That(store.Read(1)!.Data.userCategorizedGachaWishes.Length == (id == 17 ? 10 : 8), "实际8/6/2、8/5/3、10/6/1形态按总人数计算普通名额");
        }
        Reset();
        Select();
        random.Values = new(Enumerable.Repeat(0d, 10).Append(.85));
        var ten = Draw(2);
        Check.That(ten.obtainPrizes.Take(9).All(p => p.card.resourceId == 2) && ten.obtainPrizes[^1].card.resourceId == 4
            && random.Calls == 11, "十连不足三星只重抽末位，条件概率包含categorized_wish档");
        var data = store.Read(1)!.Data;
        Check.That(data.userChargedCurrency.free == 7000 && data.userGachaCeilItems.Single().quantity == 10 && data.userGachas.Single().count == 1
            && data.userCards.Single(c => c.cardId == 2).duplicateCount == 8 && data.userCharacterMissions.Single(m => m.characterMissionType == "collect_member").progress == 2
            && data.userCharacterMissions.Single(m => m.characterMissionType == "waiting_room").progress == 8, "分类十连完整结算费用、天井、计次、新卡重复卡与任务");
        Rejected(() => Select(), 409);
        Rejected(() => Draw(2), 409);
        Reset();
        Select();
        random.Values = new(new[] { .9 }.Concat(Enumerable.Repeat(0d, 9)));
        Draw(3);
        Check.That(random.Calls == 10 && store.Read(1)!.Data.userChargedCurrency.paid == 8500, "已有Wish四星不补抽，付费半价十连只扣配置费用一次");
        Reset();
        Select();
        random.Values = new([.9, .8, .5]);
        var multiple = Draw(4, count: 3);
        Check.That(multiple.obtainPrizes.Select(p => p.card.resourceId).SequenceEqual(new[] { 4, 7, 3 })
            && store.Read(1)!.Data.userGachaTickets.Single().quantity == 7 && store.Read(1)!.Data.userGachas.Single().count == 3,
            "票券多次单抽逐次保留分类与普通候选，费用和次数按实际累计");
        foreach (var kind in new[] { "partial", "duplicate", "unknown-type", "wrong-type", "stale" })
        {
            Reset();
            Select();
            var state = store.Read(1)!;
            if (kind == "partial") state.Data.userCategorizedGachaWishes = state.Data.userCategorizedGachaWishes.Take(4).ToArray();
            if (kind == "duplicate") state.Data.userCategorizedGachaWishes[4].gachaDetailId = 108;
            if (kind == "unknown-type") state.Data.userCategorizedGachaWishes[4].categorizedWishType = "unknown";
            if (kind == "wrong-type") state.Data.userCategorizedGachaWishes[4].categorizedWishType = "limited";
            if (kind == "stale") state.Data.userCategorizedGachaWishes[4].gachaDetailId = 999;
            store.Save(1, state);
            var before = DumpSerializer.Serialize(state.Data);
            Check.Throws<InvalidOperationException>(() => Draw(), "已存分类名单损坏不退回普通概率");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "坏名单不扣费发奖或改状态");
        }
        foreach (var draw in new[] { false, true })
        {
            Reset();
            if (draw) Select();
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
            {
                if (draw) gacha.ExecuteGacha(1, 1, false);
                else gacha.SaveCategorizedGachaWish(Request());
                return new GachaExtraChecks.BrokenResponse();
            }), "注册或发奖后编码失败");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败整体回滚名单或费用、卡牌、任务与次数");
        }
        Reset();
        var raced = Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            using var racing = provider.CreateScope();
            try
            {
                racing.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () =>
                { racing.ServiceProvider.GetRequiredService<GachaService>().SaveCategorizedGachaWish(Request()); return null; });
                return true;
            }
            catch (GachaRequestRejectedException e) when (e.Status == 409) { return false; }
        }))).GetAwaiter().GetResult();
        Check.That(raced.Count(success => success) == 1 && store.Read(1)!.Data.userCategorizedGachaWishes.Length == 5, "并发注册只保存一份完整名单");
        var saved = store.Read(1)!;
        var database = Path.Combine(directory, "categorized-gacha.db");
        using (var sqlite = new SqliteUserStore(database)) sqlite.Save(1, saved);
        using var reopened = new SqliteUserStore(database);
        var restored = JsonSerializer.SerializeToNode(reopened.Read(1)!.Data, DumpJson.Options)!.AsObject();
        var expected = JsonSerializer.SerializeToNode(saved.Data, DumpJson.Options)!.AsObject();
        foreach (var key in new[] { "now", "refreshableTypes" }) { restored.Remove(key); expected.Remove(key); }
        Check.That(JsonNode.DeepEquals(restored, expected), "SQLite重启保留三类名单及全部持久状态");
        using var resumed = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<IUserStore>(reopened)
            .AddSingleton<Random>(random).AddSingleton<TimeProvider>(new Clock()).BuildServiceProvider();
        using var resumedScope = resumed.CreateScope();
        random.Values = new([.95]);
        random.Pick = 4;
        var resumedResult = DumpSerializer.Deserialize<UserGachaResponse>(resumedScope.ServiceProvider.GetRequiredService<UserOperation>()
            .Execute(1, () => resumedScope.ServiceProvider.GetRequiredService<GachaService>().ExecuteGacha(1, 1, false)));
        Check.That(resumedResult.obtainPrizes.Single().card.resourceId == 9, "重启后实际使用原分类名单而非全池");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "注册和抽取不改变其他账号");
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
