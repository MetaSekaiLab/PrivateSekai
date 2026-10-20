extern alias game;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Gacha;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class GiftGachaChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "gift-gacha-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string json) => File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        var pools = JsonNode.Parse("""
            [{"id":1,"gachaType":"gift","startAt":1,"gachaCeilItemId":9,
              "gachaDetails":[{"cardId":2,"weight":1},{"cardId":4,"weight":1}],
              "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100},{"cardRarityType":"rarity_4","lotteryType":"normal","rate":1}],
              "gachaBehaviors":[{"id":1,"spinCount":10,"executeLimit":3,"gachaBehaviorType":"over_rarity_4_once","costResourceType":"paid_jewel","costResourceQuantity":3000,"gachaExtraId":1}]}]
            """)!.AsArray();
        foreach (var id in new[] { 2, 3, 4, 5, 6 })
        {
            var pool = pools[0]!.DeepClone();
            pool["id"] = id;
            if (id == 3) pool["gachaType"] = "normal";
            if (id == 4) pool["endAt"] = 1000;
            if (id == 5) pool["startAt"] = 1001;
            if (id == 6) pool["spinLimit"] = 10;
            pools.Add(pool);
        }
        Table("gachas", pools.ToJsonString());
        Table("cards", """[{"id":2,"characterId":1,"cardRarityType":"rarity_2"},{"id":4,"characterId":1,"cardRarityType":"rarity_4"}]""");
        Table("gachaExtras", """[{"id":1,"resourceBoxId":1}]""");
        Table("giftGachaExchanges", """
            [{"id":1,"gachaId":1,"resourceBoxId":1},{"id":2,"gachaId":1,"resourceBoxId":2},
             {"id":3,"gachaId":2,"resourceBoxId":1},{"id":4,"gachaId":1,"resourceBoxId":99},
             {"id":5,"gachaId":1,"resourceBoxId":3},{"id":6,"gachaId":1,"resourceBoxId":4},
             {"id":7,"gachaId":4,"resourceBoxId":1},{"id":8,"gachaId":5,"resourceBoxId":1},
             {"id":9,"gachaId":6,"resourceBoxId":1}]
            """);
        Table("resourceBoxes", """
            [{"resourceBoxPurpose":"gacha_extra","id":1,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":2}]},
             {"resourceBoxPurpose":"gift_gacha_extra","id":1,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":3}]},
             {"resourceBoxPurpose":"gift_gacha_extra","id":2,"resourceBoxType":"list","details":[
              {"resourceType":"material","resourceId":7,"resourceQuantity":1},
              {"resourceType":"practice_ticket","resourceId":1,"resourceQuantity":2},
              {"resourceType":"skill_practice_ticket","resourceId":1,"resourceQuantity":3},
              {"resourceType":"boost_item","resourceId":1,"resourceQuantity":4},
              {"resourceType":"coin","resourceQuantity":5},
              {"resourceType":"mysekai_material","resourceId":1,"resourceQuantity":5},
              {"resourceType":"mysekai_material","resourceId":2,"resourceQuantity":2},
              {"resourceType":"mysekai_material","resourceId":3,"resourceQuantity":3}]},
             {"resourceBoxPurpose":"gift_gacha_extra","id":3,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":1},{"resourceType":"unknown_gift","resourceId":1,"resourceQuantity":1}]},
             {"resourceBoxPurpose":"gift_gacha_extra","id":4,"resourceBoxType":"list","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":0}]}]
            """);
        Table("mysekaiMaterials", """[{"id":1,"mysekaiMaterialType":"wood"},{"id":2,"mysekaiMaterialType":"game_character"},{"id":3,"mysekaiMaterialType":"birthday_party"}]""");
        foreach (var table in new[] { "cardEpisodes", "cardCostume3ds", "costume3dShopItems", "releaseConditions",
            "characterArchiveVoices", "characterMissionV2s", "characterMissionV2ParameterGroups", "beginnerMissionV2s", "honorMissions" }) Table(table, "[]");
        var store = new MemoryUserStore();
        var clock = new Clock();
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<IUserStore>(store).AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        const long owner = 4294967297;
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        void Reset()
        {
            clock.At = 1000;
            var state = TestUsers.Create(owner);
            state.Data.userChargedCurrency = new() { paid = 10000 };
            state.Data.userMysekaiMaterials = [new() { mysekaiMaterialId = 1, quantity = 7, lastObtainedAt = 11 }];
            state.Data.userMysekaiMaterialPossession = new() { quantity = 7 };
            store.Save(owner, state);
        }
        SuiteUserCommonResponse Select(int exchange, int pool = 1) => DumpSerializer.Deserialize<SuiteUserCommonResponse>(operation.Execute(owner, () =>
        {
            gacha.SaveGiftGachaWish(new() { gachaId = pool, giftGachaExchangeId = exchange });
            return new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() };
        }));
        UserGachaResponse Draw(int pool = 1) => DumpSerializer.Deserialize<UserGachaResponse>(operation.Execute(owner, () =>
        {
            var result = gacha.ExecuteGacha(pool, 1, false);
            result.updatedResources = user.BuildRefresh(prepareRefresh: false);
            return result;
        }));
        void Rejected(Action action, int status)
        {
            var before = DumpSerializer.Serialize(store.Read(owner)!.Data);
            var error = Check.Throws<GachaRequestRejectedException>(action, "无效赠品选择或抽取拒绝");
            Check.That(error.Status == status && DumpSerializer.Serialize(store.Read(owner)!.Data).SequenceEqual(before),
                "拒绝不扣费、不发卡、不改变选择或其他用户状态");
        }
        Reset();
        Rejected(() => Draw(), 409);
        Rejected(() => Select(999), 400);
        Rejected(() => Select(3), 400);
        Rejected(() => Select(1, 3), 400);
        Rejected(() => Select(7, 4), 409);
        Rejected(() => Select(8, 5), 409);
        Rejected(() => Select(1, 999), 404);
        var selected = Select(1).updatedResources.userGiftGachaWishes.Single();
        Check.That(selected.gachaId == 1 && selected.giftGachaExchangeId == 1 && selected.createdAt == 1000 && selected.updatedAt == 1000
            && store.Read(owner)!.Data.userChargedCurrency.paid == 10000 && store.Read(owner)!.Data.userMaterials.Length == 0,
            "选择赠品只保存本池愿望及时间，不收费或预先发奖，64位账号不截断到旧模型int字段");
        var first = Draw();
        Check.That(first.obtainPrizes.Length == 10 && first.obtainPrizes.Any(p => p.card.resourceId == 4)
            && first.obtainGachaExtras.Single().quantity == 3 && first.obtainGachaFreebies.Length == 0
            && store.Read(owner)!.Data.userMaterials.Single().quantity == 5,
            "gift十连照常保四星，赠品箱发一次且放入obtainGachaExtras，独立固定箱按正确purpose另发");
        clock.At = 2000;
        var changed = Select(2).updatedResources.userGiftGachaWishes.Single();
        Select(3, 2);
        var second = Draw();
        var data = store.Read(owner)!.Data;
        Check.That(changed.createdAt == 1000 && changed.updatedAt == 2000 && changed.giftGachaExchangeId == 2
            && data.userGiftGachaWishes.Length == 2 && data.userGiftGachaWishes.Single(w => w.gachaId == 2).giftGachaExchangeId == 3,
            "还有次数时允许改选，保留创建时间且不同卡池各自保存");
        Check.That(second.obtainGachaExtras.Select(r => r.resourceType).SequenceEqual(new[] { "material", "practice_ticket", "skill_practice_ticket",
                "boost_item", "coin", "mysekai_material", "mysekai_material", "mysekai_material" })
            && data.userMaterials.Single().quantity == 8 && data.userPracticeTickets.Single().quantity == 2
            && data.userSkillPracticeTickets.Single().quantity == 3 && data.userBoostItems.Single().quantity == 4 && data.userGamedata.coin == 105,
            "list箱完整发出六类赠品，响应保留箱内顺序和奖励量，库存按现有数量累加");
        Check.That(data.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 1).quantity == 12
            && data.userMysekaiMaterials.All(m => m.lastObtainedAt == 2000) && data.userMysekaiMaterialPossession.quantity == 12,
            "Mysekai材料保存最后获得时间，普通材料增加占用，角色及生日材料不增加占用");
        clock.At = 3000;
        Draw();
        Check.That(store.Read(owner)!.Data.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 1).quantity == 17
            && store.Read(owner)!.Data.userMysekaiMaterialPossession.quantity == 17
            && store.Read(owner)!.Data.userChargedCurrency.paid == 1000 && store.Read(owner)!.Data.userGachas.Single().count == 3,
            "三次十连逐次消费一次费用、发一套选定赠品并累计行为次数");
        Rejected(() => Draw(), 409);
        Rejected(() => Select(1), 409);
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "赠品选择和发奖不会改动其他账号");
        var saved = store.Read(owner)!;
        var database = Path.Combine(directory, "gift.db");
        using (var sqlite = new SqliteUserStore(database)) sqlite.Save(owner, saved);
        using (var reopened = new SqliteUserStore(database))
        {
            var restored = JsonSerializer.SerializeToNode(reopened.Read(owner)!.Data, DumpJson.Options)!.AsObject();
            var expected = JsonSerializer.SerializeToNode(saved.Data, DumpJson.Options)!.AsObject();
            foreach (var key in new[] { "now", "refreshableTypes" }) { restored.Remove(key); expected.Remove(key); }
            Check.That(JsonNode.DeepEquals(restored, expected), "SQLite重启保存选择、时间、六类赠品及全部相关持久字段");
        }
        foreach (var exchange in new[] { 4, 6 })
        {
            Reset();
            var before = DumpSerializer.Serialize(store.Read(owner)!.Data);
            Check.Throws<InvalidOperationException>(() => Select(exchange), "缺失或非法赠品箱不能保存选择");
            Check.That(DumpSerializer.Serialize(store.Read(owner)!.Data).SequenceEqual(before), "无效选择不留下部分愿望状态");
        }
        Reset();
        Select(9, 6);
        Draw(6);
        Rejected(() => Select(9, 6), 409);
        Rejected(() => Draw(6), 409);
        Reset();
        Select(5);
        var prior = DumpSerializer.Serialize(store.Read(owner)!.Data);
        Check.Throws<NotSupportedException>(() => Draw(), "赠品中途遇到未支持资源时抛出");
        Check.That(DumpSerializer.Serialize(store.Read(owner)!.Data).SequenceEqual(prior), "部分发奖失败回滚费用、卡牌、次数及已经发出的固定和赠品资源");
        foreach (var kind in new[] { "material", "possession" })
        {
            Reset();
            Select(2);
            var overflow = store.Read(owner)!;
            if (kind == "material") overflow.Data.userMysekaiMaterials.Single().quantity = int.MaxValue;
            else overflow.Data.userMysekaiMaterialPossession.quantity = int.MaxValue;
            store.Save(owner, overflow);
            prior = DumpSerializer.Serialize(overflow.Data);
            Check.Throws<OverflowException>(() => Draw(), "材料库存和仓库占用均检查溢出");
            Check.That(DumpSerializer.Serialize(store.Read(owner)!.Data).SequenceEqual(prior), "Mysekai溢出回滚本次全部赠品和抽卡结算");
        }
        foreach (var draw in new[] { false, true })
        {
            Reset();
            if (draw) Select(2);
            prior = DumpSerializer.Serialize(store.Read(owner)!.Data);
            Check.Throws<MessagePackSerializationException>(() => operation.Execute(owner, () =>
            {
                if (draw) gacha.ExecuteGacha(1, 1, false);
                else gacha.SaveGiftGachaWish(new() { gachaId = 1, giftGachaExchangeId = 2 });
                return new GachaExtraChecks.BrokenResponse();
            }), "保存选择或发赠品后的响应编码失败");
            Check.That(DumpSerializer.Serialize(store.Read(owner)!.Data).SequenceEqual(prior), "编码失败不提交愿望、库存或相关抽卡变化");
        }
    }

    private sealed class Clock : TimeProvider
    {
        public long At;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(At);
    }
}
