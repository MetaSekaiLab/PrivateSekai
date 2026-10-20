extern alias game;

using System;
using System.Collections.Generic;
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

internal static class GachaFreebieChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "gacha-freebie-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string json) => File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        var pools = new JsonArray();
        foreach (var (id, group) in new[] { (1, 1), (2, 0), (3, 999), (4, 2), (5, 3), (6, 4),
            (7, 5), (8, 6), (9, 7), (10, 8), (11, 9), (12, 10) })
        {
            var pool = JsonNode.Parse("""
                {"startAt":1,"spinLimit":12,"gachaCeilItemId":9,"gachaDetails":[{"cardId":4,"weight":1}],
                 "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100}],
                 "gachaBehaviors":[{"id":1,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":1},
                  {"id":2,"spinCount":10,"costResourceType":"jewel","costResourceQuantity":3000,"gachaExtraId":1}]}
                """)!;
            pool["id"] = id;
            pool["gachaFreebieGroupId"] = group;
            pools.Add(pool);
        }
        Table("gachas", pools.ToJsonString());
        Table("cards", """[{"id":4,"characterId":1,"cardRarityType":"rarity_2"}]""");
        Table("gachaExtras", """[{"id":1,"resourceBoxId":1}]""");
        Table("gachaFreebieGroups", """
            [{"id":1,"groupId":1,"seq":2,"rarity":1,"weight":2,"resourceBoxId":2},
             {"id":2,"groupId":1,"seq":1,"rarity":3,"weight":3,"resourceBoxId":1},
             {"id":3,"groupId":1,"seq":0,"rarity":2,"weight":0,"resourceBoxId":999},
             {"id":4,"groupId":2,"rarity":1,"weight":-1,"resourceBoxId":1},
             {"id":5,"groupId":3,"rarity":1,"weight":0,"resourceBoxId":1},
             {"id":6,"groupId":4,"rarity":1,"weight":1,"resourceBoxId":999},
             {"id":7,"groupId":5,"rarity":1,"weight":1,"resourceBoxId":3},
             {"id":8,"groupId":6,"rarity":1,"weight":1,"resourceBoxId":4},
             {"id":9,"groupId":7,"rarity":1,"weight":1,"resourceBoxId":5},
             {"id":10,"groupId":8,"rarity":1,"weight":1,"resourceBoxId":6},
             {"id":11,"groupId":9,"seq":1,"rarity":3,"weight":2147483647,"resourceBoxId":1},
             {"id":12,"groupId":9,"seq":2,"rarity":1,"weight":2147483647,"resourceBoxId":2},
             {"id":13,"groupId":10,"rarity":1,"weight":1,"resourceBoxId":7}]
            """);
        Table("resourceBoxes", """
            [{"resourceBoxPurpose":"gacha_extra","id":1,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":5}]},
             {"resourceBoxPurpose":"gacha_freebie_group","id":1,"resourceBoxType":"expand","details":[{"resourceType":"gacha_ticket","resourceId":1,"resourceQuantity":2}]},
             {"resourceBoxPurpose":"gacha_freebie_group","id":2,"resourceBoxType":"expand","details":[
              {"resourceType":"material","resourceId":7,"resourceQuantity":3},{"resourceType":"gacha_ticket","resourceId":1,"resourceQuantity":1},
              {"resourceType":"boost_item","resourceId":1,"resourceQuantity":1},{"resourceType":"skill_practice_ticket","resourceId":1,"resourceQuantity":1},
              {"resourceType":"coin","resourceQuantity":4},{"resourceType":"virtual_coin","resourceQuantity":5},
              {"resourceType":"practice_ticket","resourceId":1,"resourceQuantity":1}]},
             {"resourceBoxPurpose":"gacha_freebie_group","id":3,"resourceBoxType":"expand","details":[]},
             {"resourceBoxPurpose":"gacha_freebie_group","id":4,"resourceBoxType":"lottery","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":1}]},
             {"resourceBoxPurpose":"gacha_freebie_group","id":5,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":1},{"resourceType":"unknown","resourceId":1,"resourceQuantity":1}]},
             {"resourceBoxPurpose":"gacha_freebie_group","id":6,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":0}]},
             {"resourceBoxPurpose":"gacha_freebie_group","id":7,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceQuantity":1}]}]
            """);
        Table("characterMissionV2s", """[{"id":1,"characterId":1,"characterMissionType":"collect_member","parameterGroupId":1},{"id":2,"characterId":1,"characterMissionType":"waiting_room","parameterGroupId":1}]""");
        Table("characterMissionV2ParameterGroups", """[{"id":1,"seq":1,"requirement":1}]""");
        foreach (var table in new[] { "cardEpisodes", "cardExtras", "cardCostume3ds", "costume3dShopItems", "releaseConditions", "characterArchiveVoices", "beginnerMissionV2s", "honorMissions" }) Table(table, "[]");
        var store = new MemoryUserStore();
        var random = new DrawRandom();
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<IUserStore>(store).AddSingleton<Random>(random).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        void Reset(params long[] picks)
        {
            var state = TestUsers.Create(1);
            state.Data.userChargedCurrency = new() { free = 10000 };
            store.Save(1, state);
            random.Picks = new(picks);
        }
        UserGachaResponse Draw(int behavior = 1, int pool = 1, int? count = null) =>
            DumpSerializer.Deserialize<UserGachaResponse>(operation.Execute(1, () =>
            {
                var result = gacha.ExecuteGacha(pool, behavior, false, count);
                result.updatedResources = user.BuildRefresh(prepareRefresh: false);
                return result;
            }));
        foreach (var (pick, rarity) in new[] { (0, 3), (2, 3), (3, 1), (4, 1) })
        {
            Reset(pick);
            var result = Draw();
            Check.That(result.obtainGachaFreebies.Single().rarity == rarity && result.obtainPrizes.Length == 1,
                "随机附赠以seq排序和组内权重边界抽选，零权重不参与且不占卡牌名额");
        }
        Reset(0, 2, 3, 4, 0, 2, 3, 4, 0, 4);
        var ten = Draw(2);
        var data = store.Read(1)!.Data;
        Check.That(ten.obtainGachaFreebies.Select(f => f.rarity).SequenceEqual(new[] { 3, 3, 1, 1, 3, 3, 1, 1, 3, 1 })
            && ten.obtainGachaFreebies.Count(f => f.obtainedResources.Length == 7) == 5,
            "十连逐抽独立生成十份附赠，保留抽选和箱内资源顺序，不合并同种奖品");
        Check.That(data.userGachaTickets.Single().quantity == 15 && data.userMaterials.Single().quantity == 20
            && data.userBoostItems.Single().quantity == 5 && data.userSkillPracticeTickets.Single().quantity == 5
            && data.userPracticeTickets.Single().quantity == 5 && data.userGamedata.coin == 120 && data.userGamedata.virtualCoin == 25,
            "十连随机箱的七类真实master资源全部累计，固定附赠只发一份且purpose相同ID不串箱");
        Check.That(data.userGachas.Single().count == 1 && data.userChargedCurrency.free == 7000
            && data.userGachaCeilItems.Single().quantity == 10 && data.userCards.Single().duplicateCount == 9
            && data.userCharacterMissions.Single(m => m.characterMissionType == "collect_member").progress == 1
            && data.userCharacterMissions.Single(m => m.characterMissionType == "waiting_room").progress == 9,
            "附赠不改变十连费用、行为次数、天井、卡牌及首次和重复卡任务");
        Check.That(ten.updatedResources.userGachaTickets.Single().quantity == 15
            && ten.obtainGachaFreebies[0].obtainedResources.Single().quantity == 2,
            "刷新返回实际持有余额，附赠结果保留本次奖励量并使用独立副本");
        random.Picks = new([3, 0]);
        var rest = Draw(count: 2);
        Check.That(rest.obtainGachaFreebies.Length == 2 && store.Read(1)!.Data.userGachas.Single(g => g.gachaBehaviorId == 1).count == 2
            && store.Read(1)!.Data.userGachaTickets.Single().quantity == 18 && store.Read(1)!.Data.userMaterials.Single().quantity == 33,
            "限量池剩余两抽按指定次数发两份随机附赠和两份固定箱，允许同奖再次抽中");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<GachaRequestRejectedException>(() => Draw(), "耗尽限量池不再抽选附赠");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "耗尽名额不扣费或增加任何奖励");
        Reset();
        Check.That(Draw(pool: 2).obtainGachaFreebies.Length == 0, "未配置随机附赠的普通池保持空数组");
        foreach (var pool in new[] { 3, 4, 5, 6, 7, 8, 9, 10, 12 })
        {
            Reset();
            before = DumpSerializer.Serialize(store.Read(1)!.Data);
            var failed = false;
            try { Draw(pool: pool); }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException) { failed = true; }
            Check.That(failed && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "缺组、负或全零权重、缺箱、空箱、错箱、未知资源及非法数量或ID整体回滚，不保留扣款或部分发奖");
        }
        Reset(2L * int.MaxValue - 1);
        Check.That(Draw(pool: 11).obtainGachaFreebies.Single().rarity == 1, "附赠权重求和使用长整数，不因超过int上限失效");
        foreach (var kind in new[] { "gacha_ticket", "virtual_coin", "material" })
        {
            Reset(3);
            var state = store.Read(1)!;
            if (kind == "gacha_ticket") state.Data.userGachaTickets = [new() { gachaTicketId = 1, quantity = int.MaxValue }];
            if (kind == "virtual_coin") state.Data.userGamedata.virtualCoin = int.MaxValue;
            if (kind == "material") state.Data.userMaterials = [new() { materialId = 7, quantity = int.MaxValue - 5 }];
            store.Save(1, state);
            before = DumpSerializer.Serialize(state.Data);
            Check.Throws<OverflowException>(() => Draw(), "随机附赠库存累计检查整数溢出");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "库存溢出回滚此前的固定箱、随机箱部分资源、卡牌和任务");
        }
        Reset(3);
        before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            gacha.ExecuteGacha(1, 1, false);
            return new GachaExtraChecks.BrokenResponse();
        }), "随机附赠已发放后响应编码失败");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败不提交附赠或其余用户变化");
        Reset(3);
        Draw();
        var saved = store.Read(1)!;
        var database = Path.Combine(directory, "freebie.db");
        using (var sqlite = new SqliteUserStore(database)) sqlite.Save(1, saved);
        using var reopened = new SqliteUserStore(database);
        var restored = JsonSerializer.SerializeToNode(reopened.Read(1)!.Data, DumpJson.Options)!.AsObject();
        var expected = JsonSerializer.SerializeToNode(saved.Data, DumpJson.Options)!.AsObject();
        foreach (var key in new[] { "now", "refreshableTypes" }) { restored.Remove(key); expected.Remove(key); }
        Check.That(JsonNode.DeepEquals(restored, expected), "SQLite重启保留随机附赠的全部持久库存、抽卡状态与任务");
    }

    private sealed class DrawRandom : Random
    {
        public Queue<long> Picks { get; set; } = new();
        public override double NextDouble() => 0;
        public override long NextInt64(long maxValue) => maxValue == 1 ? 0 : Picks.Dequeue();
    }
}
