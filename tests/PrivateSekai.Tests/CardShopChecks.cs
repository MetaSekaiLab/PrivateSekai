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
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Modules.Shop;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class CardShopChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "card-shop-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string value) => File.WriteAllText(Path.Combine(directory, name + ".json"), value);
        Table("cards", """[{"id":1,"characterId":21,"cardRarityType":"rarity_4"},{"id":2,"characterId":22,"cardRarityType":"rarity_4"},{"id":3,"characterId":23,"cardRarityType":"rarity_4"}]""");
        Table("materialExchangeSummaries", """
            [{"id":20,"materialExchangeType":"normal","materialExchangeFreebieGroupJson":{"id":7,"materialExchangeFreebieType":"exchange_already_owned_card"},
              "materialExchangeFreebies":[{"id":99,"materialExchangeFreebieGroupId":8,"materialExchangeFreebieTypeId":21,"resourceBoxId":999},
               {"id":1,"materialExchangeFreebieGroupId":7,"materialExchangeFreebieTypeId":21,"resourceBoxId":201},{"id":2,"materialExchangeFreebieGroupId":7,"materialExchangeFreebieTypeId":22,"resourceBoxId":202}]},
             {"id":21,"materialExchangeType":"normal","materialExchangeFreebieGroupJson":{"id":8,"materialExchangeFreebieType":"exchange_already_owned_card"},
              "materialExchangeFreebies":[{"id":1,"materialExchangeFreebieGroupId":7,"materialExchangeFreebieTypeId":21,"resourceBoxId":201}]},
             {"id":22,"materialExchangeType":"normal","materialExchangeFreebieGroupJson":{"id":7,"materialExchangeFreebieType":"exchange_already_owned_card"},
              "materialExchangeFreebies":[{"id":1,"materialExchangeFreebieGroupId":7,"materialExchangeFreebieTypeId":21,"resourceBoxId":201},{"id":2,"materialExchangeFreebieGroupId":7,"materialExchangeFreebieTypeId":21,"resourceBoxId":202}]},
             {"id":23,"materialExchangeType":"normal","materialExchangeFreebieGroupJson":{"id":7,"materialExchangeFreebieType":"exchange_already_owned_card"},
              "materialExchangeFreebies":[{"id":1,"materialExchangeFreebieGroupId":7,"materialExchangeFreebieTypeId":21,"resourceBoxId":999}]},
             {"id":24,"materialExchangeType":"normal","materialExchangeFreebieGroupJson":{"id":7,"materialExchangeFreebieType":"exchange_already_owned_card"},
              "materialExchangeFreebies":[{"id":1,"materialExchangeFreebieGroupId":7,"materialExchangeFreebieTypeId":21,"resourceBoxId":203}]},
             {"id":25,"materialExchangeType":"normal","materialExchangeFreebieGroupJson":{"id":7,"materialExchangeFreebieType":"future"}},
             {"id":26,"materialExchangeType":"normal","materialExchangeFreebies":[{"id":1,"materialExchangeFreebieGroupId":7,"materialExchangeFreebieTypeId":21,"resourceBoxId":201}]},
             {"id":27,"materialExchangeType":"normal","materialExchangeFreebieGroupJson":{"id":7,"materialExchangeFreebieType":"exchange_already_owned_card"},
              "materialExchangeFreebies":[{"id":1,"materialExchangeFreebieGroupId":7,"materialExchangeFreebieTypeId":21,"resourceBoxId":204}]},
             {"id":28,"materialExchangeType":"normal"}]
            """);
        var exchanges = new JsonArray();
        for (var id = 1; id <= 12; id++)
            exchanges.Add(new JsonObject { ["id"] = id, ["materialExchangeSummaryId"] = id <= 4 ? 20 : id + 16,
                ["resourceBoxId"] = id == 4 ? 104 : id <= 3 ? 100 + id : 101, ["refreshCycle"] = "none",
                ["costs"] = JsonNode.Parse("""[{"costGroupId":1,"resourceType":"material","resourceId":165,"quantity":1}]""") });
        Table("materialExchanges", exchanges.ToJsonString());
        Table("resourceBoxes", """
            [{"id":101,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"card","resourceId":1,"resourceQuantity":1}]},
             {"id":102,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"card","resourceId":2,"resourceQuantity":1}]},
             {"id":103,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"card","resourceId":3,"resourceQuantity":1}]},
             {"id":104,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"card","resourceId":1,"resourceQuantity":2}]},
             {"id":201,"resourceBoxPurpose":"other","details":[{"resourceType":"material","resourceId":99,"resourceQuantity":999}]},
             {"id":201,"resourceBoxPurpose":"material_exchange_freebie","resourceBoxType":"expand","details":[
              {"resourceType":"material","resourceId":60,"resourceQuantity":1},{"resourceType":"material","resourceId":133,"resourceQuantity":1},
              {"resourceType":"skill_practice_ticket","resourceId":10103,"resourceQuantity":1},{"resourceType":"practice_ticket","resourceId":10103,"resourceQuantity":1}]},
             {"id":202,"resourceBoxPurpose":"material_exchange_freebie","resourceBoxType":"list","details":[{"resourceType":"material","resourceId":61,"resourceQuantity":3}]},
             {"id":203,"resourceBoxPurpose":"material_exchange_freebie","resourceBoxType":"expand","details":[{"resourceType":"future","resourceId":1,"resourceQuantity":1}]},
             {"id":204,"resourceBoxPurpose":"material_exchange_freebie","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":60,"resourceQuantity":0}]},
             {"id":1,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"custom_profile_collection_resource","resourceId":821,"resourceQuantity":1},{"resourceType":"stamp","resourceId":878,"resourceQuantity":1}]}]
            """);
        Table("cardExtras", """[{"id":1,"cardId":1,"resourceBoxId":1}]""");
        Table("stamps", """[{"id":878,"characterId1":21}]""");
        Table("cardCostume3ds", """[{"cardId":1,"costume3dId":10}]""");
        Table("costume3ds", """[{"id":10,"characterId":21,"partType":"body"}]""");
        Table("releaseConditions", """[{"id":500,"releaseConditionType":"master_rank","releaseConditionTypeId":1,"releaseConditionTypeLevel":0}]""");
        Table("characterMissionV2s", """
            [{"id":1,"characterId":21,"characterMissionType":"collect_member","parameterGroupId":1},
             {"id":2,"characterId":21,"characterMissionType":"waiting_room","parameterGroupId":1},
             {"id":3,"characterId":21,"characterMissionType":"collect_stamp","parameterGroupId":1},
             {"id":4,"characterId":21,"characterMissionType":"collect_costume_3d","parameterGroupId":1}]
            """);
        Table("characterMissionV2ParameterGroups", """[{"id":1,"seq":1,"requirement":1}]""");
        Table("honorMissions", """[{"id":1,"honorMissionType":"duplicate_card_total","requirement":1},{"id":2,"honorMissionType":"collect_stamp","requirement":1},{"id":3,"honorMissionType":"collect_costume_3d","requirement":1}]""");
        foreach (var table in new[] { "cardEpisodes", "costume3dShopItems", "characterArchiveVoices", "beginnerMissionV2s" }) Table(table, "[]");
        var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
        var store = new MemoryUserStore();
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<IUserStore>(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var shop = scope.ServiceProvider.GetRequiredService<ShopService>();
        var missions = scope.ServiceProvider.GetRequiredService<MissionMasterQueries>();
        void Reset()
        {
            var state = TestUsers.Create(1);
            state.Data.userCards = [];
            state.Data.userMaterials = [new() { materialId = 165, quantity = 100 }];
            state.Data.userPracticeTickets = [];
            state.Data.userSkillPracticeTickets = [];
            state.Data.userMaterialExchanges = [];
            state.Data.userCharacterMissions = [];
            state.Data.userCharacterMissionStatuses = [];
            state.Data.userHonorMissions = [];
            state.Data.userCostume3dStatuses = [];
            state.Data.userCustomProfileResources = [];
            state.Data.userStamps = [];
            store.Save(1, state);
        }
        UserExchangeResponse Exchange(int id = 1, int count = 1)
        {
            return DumpSerializer.Deserialize<UserExchangeResponse>(operations.Execute(1, () => {
                var mapping = new CardAcquisitionResponse(user.Data);
                var status = shop.ExchangeMaterial(id, 1, count);
                if (status != 200) throw new InvalidOperationException("Unexpected card shop rejection: " + status);
                var result = new UserExchangeResponse { updatedResources = user.BuildRefresh(prepareRefresh: false), releasedActionSetIds = [] };
                mapping.AddHints(result.updatedResources, missions);
                return result;
            }));
        }
        Reset();
        var first = Exchange();
        var data = store.Read(1)!.Data;
        Check.That(data.userCards.Single().duplicateCount == 0 && data.userMaterials.Single().quantity == 99
            && data.userPracticeTickets.Length == 0 && data.userSkillPracticeTickets.Length == 0, "首次兑换成员扣费并获得卡牌，尚不发重复赠品");
        Check.That(data.userCostume3dStatuses.Single().status == "available" && data.userStamps.Single().stampId == 878
            && data.userCustomProfileResources.Single().quantity == 1, "成员兑换复用初始服装、卡牌附赠和收集联动");
        Check.That(first.updatedResources.newReleaseConditions.Single().releaseConditionId == 500
            && first.updatedResources.userCharacterMissions.Any(m => m.achievedMissions.Length > 0)
            && first.updatedResources.userHonorMissions.Any(m => m.achievedMissionIds.Length > 0), "首次兑换响应含本次条件、角色任务和称号提示");
        Check.That(data.newReleaseConditions == null && data.userCharacterMissions.All(m => m.achievedMissions.Length == 0)
            && data.userHonorMissions.All(m => m.achievedMissionIds.Length == 0), "本次达成提示不进入持久状态");
        var second = Exchange();
        data = store.Read(1)!.Data;
        Check.That(data.userCards.Single().duplicateCount == 1 && data.userMaterials.Single(m => m.materialId == 60).quantity == 1
            && data.userMaterials.Single(m => m.materialId == 133).quantity == 1 && data.userSkillPracticeTickets.Single().quantity == 1
            && data.userPracticeTickets.Single().quantity == 1, "重复兑换按赠品组和角色发完整四项箱，忽略同号其他用途");
        Check.That(data.userCustomProfileResources.Single().quantity == 2 && data.userStamps.Length == 1 && data.userCostume3dStatuses.Length == 1
            && second.updatedResources.userHonorMissions.Single(m => m.honorMissionType == "duplicate_card_total").achievedMissionIds.SequenceEqual(new[] { 1 }), "重复卡素材继续增加，唯一附赠只一份且重复称号本次提示");
        Reset();
        Exchange(count: 3);
        data = store.Read(1)!.Data;
        Check.That(data.userCards.Single().duplicateCount == 2 && data.userMaterials.Single(m => m.materialId == 60).quantity == 2
            && data.userPracticeTickets.Single().quantity == 2 && data.userCustomProfileResources.Single().quantity == 3
            && data.userCharacterMissions.Single(m => m.characterMissionType == "waiting_room").progress == 2, "新卡批量三张只首张无赠品，其余两张均算重复");
        Reset();
        Exchange(4, 2);
        Check.That(store.Read(1)!.Data.userCards.Single().duplicateCount == 3 && store.Read(1)!.Data.userPracticeTickets.Single().quantity == 3,
            "资源箱卡数与兑换次数共同计算获得张数及重复赠品");
        Reset();
        Exchange(2);
        Exchange(2, 2);
        Check.That(store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 61).quantity == 6,
            "相同稀有度的另一角色使用独立角色映射，list箱也完整发放");
        Reset();
        Exchange(12, 2);
        Check.That(store.Read(1)!.Data.userCards.Single().duplicateCount == 1 && store.Read(1)!.Data.userPracticeTickets.Length == 0,
            "无赠品配置的普通成员兑换仍获得卡牌、服装和附赠");
        foreach (var id in new[] { 3, 5, 6, 7, 8, 10, 11 })
        {
            Reset();
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<InvalidOperationException>(() => Exchange(id), "缺失角色映射、错组、重复映射、缺箱或非法赠品配置拒绝");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "坏赠品配置回滚费用和全部卡牌、库存、任务及计数");
        }
        Reset();
        var unsupported = DumpSerializer.Serialize(store.Read(1)!.Data);
        var code = 0;
        operations.Execute(1, () => { code = shop.ExchangeMaterial(9, 1, 1); return null; });
        Check.That(code == 501 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(unsupported), "未知赠品类型不静默跳过后成功");
        Exchange();
        var overflow = store.Read(1)!;
        overflow.Data.userPracticeTickets = [new() { practiceTicketId = 10103, quantity = int.MaxValue }];
        store.Save(1, overflow);
        var snapshot = DumpSerializer.Serialize(overflow.Data);
        Check.Throws<OverflowException>(() => Exchange(), "赠品最后一项库存溢出");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot), "后项溢出回滚先发材料、技能券、卡牌附赠、任务和扣费");
        Reset();
        snapshot = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () => { shop.ExchangeMaterial(1, 1, 2); return new GachaExtraChecks.BrokenResponse(); }), "成员兑换后编码失败");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot), "编码失败回滚卡、服装、赠品及全部状态");
        Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => {
            using var race = provider.CreateScope();
            race.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () => race.ServiceProvider.GetRequiredService<ShopService>().ExchangeMaterial(1, 1, 1));
        }))).GetAwaiter().GetResult();
        data = store.Read(1)!.Data;
        Check.That(data.userCards.Single().duplicateCount == 1 && data.userPracticeTickets.Single().quantity == 1
            && data.userMaterialExchanges.Single().totalExchangeCount == 2, "并发首次兑换仅一次为新卡、一次为重复赠品");
        var database = Path.Combine(directory, "card-shop.db");
        using (var sqlite = new SqliteUserStore(database)) sqlite.Save(1, store.Read(1)!);
        using var restored = new SqliteUserStore(database);
        using var resumed = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<IUserStore>(restored).BuildServiceProvider();
        using var resumedScope = resumed.CreateScope();
        resumedScope.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () => resumedScope.ServiceProvider.GetRequiredService<ShopService>().ExchangeMaterial(1, 1, 2));
        data = restored.Read(1)!.Data;
        Check.That(data.userCards.Single().duplicateCount == 3 && data.userPracticeTickets.Single().quantity == 3
            && data.userCustomProfileResources.Single().quantity == 4 && data.userMaterialExchanges.Single().totalExchangeCount == 4,
            "SQLite重启后重复兑换继续累计卡牌、赠品、附赠及次数");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "成员兑换不修改其他账号");
    }
}
