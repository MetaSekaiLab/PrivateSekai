extern alias game;

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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

internal static class CardExtraChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "card-extra-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string value) => File.WriteAllText(Path.Combine(directory, name + ".json"), value);
        var pool = JsonNode.Parse("""
            {"id":1,"startAt":1,"gachaBonusId":1,"gachaCeilItemId":9,"gachaDetails":[],
             "gachaCardRarityRates":[{"cardRarityType":"rarity_4","lotteryType":"normal","rate":100}],
             "gachaBehaviors":[{"id":1,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300},
              {"id":2,"spinCount":10,"costResourceType":"paid_jewel","costResourceQuantity":1500},
              {"id":3,"spinCount":1,"costResourceType":"gacha_ticket","costResourceId":1,"costResourceQuantity":1}]}
            """)!;
        var cardRows = new JsonArray();
        for (var id = 1; id <= 12; id++)
        {
            pool["gachaDetails"]!.AsArray().Add(new JsonObject { ["id"] = 100 + id, ["cardId"] = id, ["weight"] = 1 });
            cardRows.Add(new JsonObject { ["id"] = id, ["characterId"] = 1, ["cardRarityType"] = "rarity_4" });
        }
        Table("gachas", new JsonArray(pool).ToJsonString());
        Table("cards", cardRows.ToJsonString());
        Table("gachaBonuses", """[{"id":1,"switchFixedBonusWeight":false}]""");
        Table("clientConfigs", """[{"id":113,"type":"Float","value":"0.5"},{"id":114,"type":"Float","value":"1"}]""");
        Table("cardExtras", """
            [{"id":1,"cardId":1,"resourceBoxId":1},{"id":2,"cardId":2,"resourceBoxId":2},{"id":3,"cardId":3,"resourceBoxId":3},
             {"id":5,"cardId":5,"resourceBoxId":999},{"id":6,"cardId":6,"resourceBoxId":6},{"id":7,"cardId":7,"resourceBoxId":7},
             {"id":8,"cardId":8,"resourceBoxId":1},{"id":9,"cardId":8,"resourceBoxId":2},{"id":10,"cardId":9,"resourceBoxId":9},
             {"id":11,"cardId":10,"resourceBoxId":10},{"id":12,"cardId":11,"resourceBoxId":11},{"id":13,"cardId":12,"resourceBoxId":12}]
            """);
        Table("resourceBoxes", """
            [{"id":1,"resourceBoxPurpose":"gacha_extra","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":999}]},
             {"id":1,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"avatar_accessory","resourceId":81,"resourceQuantity":1}]},
             {"id":2,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"custom_profile_collection_resource","resourceId":821,"resourceQuantity":1},{"resourceType":"stamp","resourceId":878,"resourceQuantity":1}]},
             {"id":3,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"mysekai_fixture","resourceId":837,"resourceQuantity":1}]},
             {"id":6,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"unknown","resourceId":1,"resourceQuantity":1}]},
             {"id":7,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"custom_profile_collection_resource","resourceId":822,"resourceQuantity":1},{"resourceType":"custom_profile_collection_resource","resourceId":823,"resourceQuantity":2}]},
             {"id":9,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"custom_profile_collection_resource","resourceId":824,"resourceQuantity":1},{"resourceType":"mysekai_fixture","resourceId":999,"resourceQuantity":1}]},
             {"id":10,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"custom_profile_collection_resource","resourceId":825,"resourceQuantity":1},{"resourceType":"stamp","resourceId":999,"resourceQuantity":1}]},
             {"id":11,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"stamp","resourceId":878,"resourceQuantity":2}]},
             {"id":12,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[]},
             {"id":101,"resourceBoxPurpose":"gacha_ceil_exchange","details":[{"resourceType":"card","resourceId":2,"resourceQuantity":1}]},
             {"id":102,"resourceBoxPurpose":"gacha_ceil_exchange","details":[{"resourceType":"card","resourceId":3,"resourceQuantity":1}]}]
            """);
        Table("gachaCeilExchangeSummaries", """
            [{"id":1,"startAt":1,"gachaCeilExchanges":[
             {"id":1,"startAt":1,"resourceBoxId":101,"gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","gachaCeilItemId":9,"quantity":1}},
             {"id":2,"startAt":1,"resourceBoxId":102,"gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","gachaCeilItemId":9,"quantity":1}}]}]
            """);
        Table("mysekaiFixtures", """[{"id":837,"mysekaiFixtureType":"normal"}]""");
        Table("stamps", """[{"id":878,"characterId1":1}]""");
        Table("cardCostume3ds", """[{"cardId":2,"costume3dId":10}]""");
        Table("costume3ds", """[{"id":10,"characterId":1,"partType":"body"}]""");
        Table("characterMissionV2s", """[{"id":1,"characterId":1,"characterMissionType":"collect_stamp","parameterGroupId":1},{"id":2,"characterId":1,"characterMissionType":"collect_member","parameterGroupId":1},{"id":3,"characterId":1,"characterMissionType":"waiting_room","parameterGroupId":1}]""");
        Table("characterMissionV2ParameterGroups", """[{"id":1,"seq":1,"requirement":1}]""");
        Table("honorMissions", """[{"id":1,"honorMissionType":"collect_stamp","requirement":1}]""");
        foreach (var table in new[] { "cardEpisodes", "costume3dShopItems", "releaseConditions", "characterArchiveVoices", "beginnerMissionV2s" }) Table(table, "[]");
        var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
        var clock = new Clock();
        var random = new DrawRandom();
        var store = new MemoryUserStore();
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<TimeProvider>(clock)
            .AddSingleton<IUserStore>(store).AddSingleton<Random>(random).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        void Reset(int card = 1)
        {
            var state = TestUsers.Create(1);
            state.Data.userCards = [];
            state.Data.userAvatarAccessories = [];
            state.Data.userStamps = [];
            state.Data.userCustomProfileResources = [];
            state.Data.userMysekaiFixtures = [];
            state.Data.userCostume3dStatuses = [];
            state.Data.userChargedCurrency = new() { free = 100000, paid = 100000 };
            state.Data.userGachaTickets = [new() { gachaTicketId = 1, quantity = 100 }];
            state.Data.userGachaCeilItems = [new() { gachaCeilItemId = 9, quantity = 100 }];
            state.Data.userHonorMissions = [];
            state.Data.userCharacterMissions = [];
            state.Data.userCharacterMissionStatuses = [];
            store.Save(1, state);
            random.Pick = card - 1;
            clock.Now = 1000;
        }
        UserGachaResponse Draw(int behavior = 1, int? count = null) => DumpSerializer.Deserialize<UserGachaResponse>(operation.Execute(1, () => {
            var response = gacha.ExecuteGacha(1, behavior, false, count);
            response.updatedResources = user.BuildRefresh(prepareRefresh: false);
            return response;
        }));
        UserGachaCeilExchangeResponse Exchange(int id, int count = 1) => DumpSerializer.Deserialize<UserGachaCeilExchangeResponse>(operation.Execute(1, () => {
            var response = gacha.ExchangeGachaCeilItem(new() { gachaCeilExchangeRequest = new() { gachaExchangeId = id, exchangeCount = count } });
            response.updatedResources = user.BuildRefresh(prepareRefresh: false);
            return response;
        }));
        Reset();
        Check.That(Draw().obtainPrizes.Single().cardExtra.Single().resourceId == 81 && store.Read(1)!.Data.userAvatarAccessories.Single().avatarAccessoryId == 81,
            "特定卡牌按card_extra用途实际获得头像饰品，忽略同号其他用途箱");
        Check.That(Draw().obtainPrizes.Single().cardExtra.Length == 0 && store.Read(1)!.Data.userCards.Single().duplicateCount == 1,
            "重复卡仍累计候场室，但不重复发已持有头像饰品");
        Reset(2);
        var first = Draw().obtainPrizes.Single();
        Check.That(first.cardExtra.Select(r => r.resourceType).SequenceEqual(new[] { "custom_profile_collection_resource", "stamp" }) && first.costume3d.Single().resourceId == 10,
            "首次卡牌同时发初始服装及名片素材、贴图，保持箱内顺序");
        var second = Draw().obtainPrizes.Single();
        Check.That(!second.newFlg && second.cardExtra.Single().resourceType == "custom_profile_collection_resource" && second.costume3d.Length == 0,
            "重复卡仍发可累计名片素材，不重复发贴图或服装");
        var ten = Draw(2);
        var data = store.Read(1)!.Data;
        Check.That(ten.obtainPrizes.Length == 10 && ten.obtainPrizes.All(p => p.cardExtra.Length == 1)
            && data.userCustomProfileResources.Single().quantity == 12 && data.userStamps.Length == 1 && data.userCards.Single().duplicateCount == 11,
            "跨单抽和十连逐张累计名片素材，唯一贴图只一份");
        Check.That(data.userHonorMissions.Single(h => h.honorMissionType == "collect_stamp").progress == 1
            && data.userCharacterMissions.Single(m => m.characterMissionType == "collect_stamp").progress == 1, "首次附赠贴图推进称号和所属角色任务，重复不计");
        Reset(3);
        Check.That(Draw().obtainPrizes.Single().cardExtra.Single().resourceType == "mysekai_fixture", "联动家具进入cardExtra实际奖品");
        clock.Now = 2000;
        Check.That(Draw().obtainPrizes.Single().cardExtra.Length == 0 && store.Read(1)!.Data.userMysekaiFixtures.Single().quantity == 1
            && store.Read(1)!.Data.userMysekaiFixtures.Single().textureId == 1 && store.Read(1)!.Data.userMysekaiFixtures.Single().lastObtainedAt == 1000,
            "家具以默认纹理及实际获得时间落库，重复卡不重发或更新时间");
        Reset(2);
        var owned = store.Read(1)!;
        owned.Data.userCards = [new() { cardId = 2, userId = 1, level = 1 }];
        store.Save(1, owned);
        Check.That(Draw().obtainPrizes.Single().cardExtra.Length == 2, "按附赠资源是否持有判断，卡牌已有而附赠缺失时仍实际发奖");
        Reset(4);
        Check.That(Draw().obtainPrizes.Single().cardExtra.Length == 0, "普通无附赠卡牌保持空数组");
        Reset(3);
        owned = store.Read(1)!;
        owned.Data.userGachaBonusPoints = [new() { gachaId = 1, gachaBonusPoint = 49.5f, totalGachaBonusPoint = 49.5f }];
        store.Save(1, owned);
        var bonus = Draw().obtainPrizes.Single();
        Check.That(bonus.gachaLotteryType == "fixed_gacha_bonus" && bonus.cardExtra.Single().resourceId == 837, "保底选中的卡牌也完整发附赠");
        Reset(2);
        Check.That(Draw(3, 5).obtainPrizes.Sum(p => p.cardExtra.Count(r => r.resourceType == "custom_profile_collection_resource")) == 5
            && store.Read(1)!.Data.userGachaTickets.Single().quantity == 95, "指定票券多抽逐张结算附赠并正确扣票");
        Reset();
        var exchange = Exchange(1, 3);
        data = store.Read(1)!.Data;
        Check.That(exchange.obtainUserResources[0].resourceType == "card" && exchange.obtainUserResources[0].quantity == 3
            && exchange.obtainUserResources.Single(r => r.resourceType == "custom_profile_collection_resource").quantity == 3
            && exchange.obtainUserResources.Single(r => r.resourceType == "stamp").quantity == 1
            && exchange.obtainUserResources.Single(r => r.resourceType == "costume_3d").quantity == 1
            && data.userCards.Single().duplicateCount == 2 && data.userGachaCeilItems.Single().quantity == 97,
            "天井多次换卡原卡资源在前，附赠及初始服装一并结算，素材按换卡张数增加");
        Check.That(Exchange(1).obtainUserResources.Length == 2 && store.Read(1)!.Data.userCustomProfileResources.Single().quantity == 4,
            "重复换同卡仍获得素材，已持有贴图和服装不返回重复奖励");
        Exchange(2);
        Check.That(Exchange(2).obtainUserResources.Single().resourceType == "card" && store.Read(1)!.Data.userMysekaiFixtures.Single().quantity == 1,
            "抽取与换卡共享家具唯一规则");
        foreach (var card in new[] { 5, 6, 8, 9, 10, 11, 12 })
        {
            Reset(card);
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<InvalidOperationException>(() => Draw(), "坏附赠映射、类型、箱、数量或后项主表拒绝");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "附赠失败回滚卡牌、服装、费用、先前奖励和全部状态");
        }
        Reset(7);
        owned = store.Read(1)!;
        owned.Data.userCustomProfileResources = [new() { customProfileResourceType = "collection", customProfileResourceId = 823, quantity = int.MaxValue }];
        store.Save(1, owned);
        var overflow = DumpSerializer.Serialize(owned.Data);
        Check.Throws<OverflowException>(() => Draw(), "第二项名片素材库存溢出");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(overflow), "后项溢出回滚已发首项、卡和费用");
        Reset(2);
        owned = store.Read(1)!;
        owned.Data.userCustomProfileResources = [new() { customProfileResourceType = "collection", customProfileResourceId = 821, quantity = int.MaxValue }];
        store.Save(1, owned);
        overflow = DumpSerializer.Serialize(owned.Data);
        Check.Throws<OverflowException>(() => Exchange(1, 2), "换卡附赠库存溢出");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(overflow), "换卡附赠失败回滚天井扣除、换卡计次、服装、卡和任务");
        Reset(2);
        owned = store.Read(1)!;
        owned.Data.userCards = [new() { cardId = 2, userId = 1, duplicateCount = int.MaxValue }];
        store.Save(1, owned);
        overflow = DumpSerializer.Serialize(owned.Data);
        Check.Throws<OverflowException>(() => Draw(), "重复卡数量溢出不能静默回绕后发附赠");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(overflow), "重复数溢出回滚费用、附赠和卡牌状态");
        Reset(2);
        var encoding = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () => { gacha.ExecuteGacha(1, 1, false); return new GachaExtraChecks.BrokenResponse(); }), "附赠后响应编码失败");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(encoding), "编码失败回滚贴图、素材、服装、任务及扣款");
        Reset(2);
        Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => {
            using var racing = provider.CreateScope();
            racing.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () => racing.ServiceProvider.GetRequiredService<GachaService>().ExecuteGacha(1, 1, false));
        }))).GetAwaiter().GetResult();
        Check.That(store.Read(1)!.Data.userCustomProfileResources.Single().quantity == 2 && store.Read(1)!.Data.userStamps.Length == 1,
            "并发获得同卡素材逐次累计且唯一贴图只发一次");
        random.Pick = 2;
        Draw();
        var saved = store.Read(1)!;
        var database = Path.Combine(directory, "card-extra.db");
        using (var sqlite = new SqliteUserStore(database)) sqlite.Save(1, saved);
        using var restored = new SqliteUserStore(database);
        Check.That(restored.Read(1)!.Data.userCustomProfileResources.Single().quantity == 2 && restored.Read(1)!.Data.userMysekaiFixtures.Single().textureId == 1
            && restored.Read(1)!.Data.userStamps.Single().stampId == 878, "SQLite重启保留名片素材、贴图与默认纹理家具");
        random.Pick = 1;
        using var resumed = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<TimeProvider>(clock)
            .AddSingleton<IUserStore>(restored).AddSingleton<Random>(random).BuildServiceProvider();
        using var resumedScope = resumed.CreateScope();
        var again = DumpSerializer.Deserialize<UserGachaResponse>(resumedScope.ServiceProvider.GetRequiredService<UserOperation>().Execute(1,
            () => resumedScope.ServiceProvider.GetRequiredService<GachaService>().ExecuteGacha(1, 1, false)));
        Check.That(again.obtainPrizes.Single().cardExtra.Single().resourceType == "custom_profile_collection_resource"
            && restored.Read(1)!.Data.userCustomProfileResources.Single().quantity == 3 && restored.Read(1)!.Data.userStamps.Length == 1,
            "SQLite重启后继续获得同卡，素材继续增加且贴图仍不重复");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "卡牌附赠不修改其他账号");
    }
    private sealed class DrawRandom : Random
    {
        public int Pick;
        public override double NextDouble() => 0;
        public override long NextInt64(long maxValue) => Math.Min(Pick, maxValue - 1);
    }
    private sealed class Clock : TimeProvider
    {
        public long Now = 1000;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
}
