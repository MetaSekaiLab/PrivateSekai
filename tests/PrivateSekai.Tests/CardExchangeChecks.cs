extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class CardExchangeChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "card-exchange-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "cards.json"), """
            [{"id":2,"cardRarityType":"rarity_2"},{"id":3,"cardRarityType":"rarity_3"},{"id":4,"cardRarityType":"rarity_4"},
             {"id":5,"cardRarityType":"rarity_birthday"},{"id":6,"cardRarityType":"bad"},{"id":7,"cardRarityType":"missing"},
             {"id":8,"cardRarityType":"rarity_2"}]
            """);
        File.WriteAllText(Path.Combine(directory, "cardExchangeResources.json"), """
            [{"cardRarityType":"rarity_2","seq":1,"resourceBoxId":1},{"cardRarityType":"rarity_3","seq":2,"resourceBoxId":6},
             {"cardRarityType":"rarity_3","seq":1,"resourceBoxId":2},
             {"cardRarityType":"rarity_4","seq":1,"resourceBoxId":3},{"cardRarityType":"rarity_birthday","seq":1,"resourceBoxId":4},
             {"cardRarityType":"bad","seq":1,"resourceBoxId":5}]
            """);
        File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
            [{"id":1,"resourceBoxPurpose":"card_exchange_resource","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":5}]},
             {"id":2,"resourceBoxPurpose":"card_exchange_resource","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":30}]},
             {"id":3,"resourceBoxPurpose":"card_exchange_resource","details":[{"resourceType":"material","resourceId":16,"resourceQuantity":1}]},
             {"id":4,"resourceBoxPurpose":"card_exchange_resource","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":300}]},
             {"id":5,"resourceBoxPurpose":"card_exchange_resource","details":[{"resourceType":"unsupported","resourceId":1,"resourceQuantity":1}]},
             {"id":6,"resourceBoxPurpose":"card_exchange_resource","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":20}]}]
            """);
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userCards = [new() { cardId = 3, duplicateCount = 2 },
            new() { userId = 1, cardId = 2, duplicateCount = 3, level = 7, totalExp = 55, masterRank = 1,
                episodes = [new() { cardEpisodeId = 20, scenarioStatus = "already_read" }] },
            new() { cardId = 4, duplicateCount = 1 }, new() { cardId = 5, duplicateCount = 1 },
            new() { cardId = 6, duplicateCount = 1 }, new() { cardId = 7, duplicateCount = 1 }];
        state.Data.userMaterials = [new() { materialId = 15, quantity = 3 }];
        state.Data.userDecks = [new() { deckId = 1, member1 = 2, leader = 2 }];
        state.Data.userCharacterMissions = [new() { characterId = 1, characterMissionType = "waiting_room", progress = 28 }];
        state.Data.userHonorMissions = [new() { honorMissionType = "duplicate_card_total", progress = 28 }];
        state.Data.refreshableTypes = ["userMaterials"];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<IUserStore>(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();
        UserCard Entry(int id, int remaining) => new() { cardId = id, duplicateCount = remaining };
        SuiteUser Exchange(UserCard[]? entries) => DumpSerializer.Deserialize<SuiteUser>(operations.Execute(1, () =>
        {
            cards.ExchangeCards(entries);
            return user.BuildRefresh();
        }));
        void Reject(UserCard[]? entries, int status, int fieldErrors = 0)
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            var error = Check.Throws<CardExchangeRejectedException>(() => Exchange(entries), "非法候场室兑换拒绝");
            Check.That(error.Status == status && (error.Fields?.Count ?? 0) == fieldErrors &&
                DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "拒绝保留卡牌、材料、任务和待刷新字段");
        }
        Reject(null, 500);
        Reject([null!], 500);
        Reject([Entry(2, -1)], 400, 1);
        Reject([Entry(2, -1), Entry(8, -1)], 400, 2);
        Reject([Entry(int.MaxValue, 0)], 400);
        Reject([Entry(0, 0)], 400);
        Reject([Entry(8, 0)], 404);
        Reject([Entry(2, 3), Entry(2, 3)], 404);
        Reject([Entry(2, 3)], 409);
        Reject([Entry(2, 4)], 409);
        Reject([Entry(2, 2), Entry(8, 0)], 404);
        Reject([Entry(2, 2), Entry(3, 2)], 409);
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<InvalidOperationException>(() => Exchange([Entry(7, 0)]), "缺少奖励配置不能消耗重复卡");
        Check.Throws<NotSupportedException>(() => Exchange([Entry(2, 2), Entry(6, 0)]), "批量后项奖励失败回滚前项扣发");
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            cards.ExchangeCards([Entry(2, 2), Entry(3, 0)]);
            return new BrokenResponse();
        }), "批量兑换响应失败回滚全部扣发");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "配置、资源或编码失败不保留任何部分结算");
        Check.That(Exchange([]).userCards == null && store.Read(1)!.Data.userCards[0].cardId == 3,
            "空列表成功但不刷新、重排或修改卡牌");
        var result = Exchange([Entry(2, 1), Entry(3, 0), Entry(4, 0), Entry(5, 0)]);
        state = store.Read(1)!;
        var saved = state.Data.userCards.Single(c => c.cardId == 2);
        Check.That(result.userMaterials.Single(m => m.materialId == 15).quantity == 413 &&
            result.userMaterials.Single(m => m.materialId == 16).quantity == 1 && saved.duplicateCount == 1,
            "二星、三星、四星和生日卡按实际减少数量和全部资源箱批量发奖");
        Check.That(saved.level == 7 && saved.totalExp == 55 && saved.masterRank == 1 && saved.episodes.Single().scenarioStatus == "already_read" &&
            state.Data.userDecks.Single().member1 == 2 && state.Data.userCards.Length == 6,
            "请求中的默认字段不覆盖卡牌养成或剧情，不移除主卡或编队成员");
        Check.That(state.Data.userCharacterMissions.Single().progress == 28 && state.Data.userHonorMissions.Single().progress == 28 &&
            result.userCharacterMissions == null && result.userHonorMissions == null, "兑换不减少历史候场室和称号进度，也不重复累计");
        Reject([Entry(2, 1)], 409);
        Exchange([Entry(2, 0)]);
        Reject([Entry(2, 0)], 409);
        state = store.Read(1)!;
        var path = Path.Combine(directory, "users.db");
        using (var sql = new SqliteUserStore(path)) sql.Save(1, state);
        using (var sql = new SqliteUserStore(path))
            Check.That(sql.Read(1)!.Data.userCards.Single(c => c.cardId == 2).duplicateCount == 0 &&
                sql.Read(1)!.Data.userMaterials.Single(m => m.materialId == 15).quantity == 418,
                "数据库重启保留重复卡清零和材料累计");
        state.Data.userCards.Single(c => c.cardId == 2).duplicateCount = int.MaxValue;
        store.Save(1, state);
        before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<OverflowException>(() => Exchange([Entry(2, 0)]), "异常超大数量不能溢出后继续发奖");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "数量溢出整体回滚");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "候场室兑换及拒绝不修改其他账号");
    }
}
