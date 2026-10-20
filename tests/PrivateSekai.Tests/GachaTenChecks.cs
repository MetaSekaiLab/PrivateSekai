extern alias game;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

internal static class GachaTenChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "gacha-ten-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "gachas.json"), """
            [{"id":1,"startAt":1,"gachaCeilItemId":9,"gachaDetails":[{"cardId":2,"weight":1},{"cardId":3,"weight":1}],
              "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100},{"cardRarityType":"rarity_3","lotteryType":"normal","rate":1}],
              "gachaBehaviors":[{"id":1,"gachaBehaviorType":"over_rarity_3_once","spinCount":10,"costResourceType":"jewel","costResourceQuantity":3000},
                {"id":2,"gachaBehaviorType":"over_rarity_3_once","spinCount":10,"costResourceType":"gacha_ticket","costResourceId":1,"costResourceQuantity":10}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "cards.json"), """
            [{"id":2,"characterId":1,"cardRarityType":"rarity_2"},{"id":3,"characterId":1,"cardRarityType":"rarity_3"}]
            """);
        File.WriteAllText(Path.Combine(directory, "cardEpisodes.json"), """[{"id":20,"cardId":2},{"id":21,"cardId":2},{"id":30,"cardId":3},{"id":31,"cardId":3}]""");
        File.WriteAllText(Path.Combine(directory, "cardCostume3ds.json"), "[]");
        File.WriteAllText(Path.Combine(directory, "cardExtras.json"), "[]");
        File.WriteAllText(Path.Combine(directory, "characterMissionV2s.json"), """
            [{"id":1,"characterId":1,"characterMissionType":"collect_member","parameterGroupId":1},
             {"id":2,"characterId":1,"characterMissionType":"waiting_room","parameterGroupId":2},
             {"id":3,"characterId":1,"characterMissionType":"collect_character_archive_voice","parameterGroupId":3}]
            """);
        File.WriteAllText(Path.Combine(directory, "characterMissionV2ParameterGroups.json"), """
            [{"id":1,"seq":1,"requirement":2},{"id":2,"seq":1,"requirement":5},{"id":3,"seq":1,"requirement":1}]
            """);
        File.WriteAllText(Path.Combine(directory, "characterArchiveVoices.json"), """
            [{"id":1,"groupId":1,"gameCharacterId":1,"characterArchiveVoiceType":"collect_card","externalId":3}]
            """);
        File.WriteAllText(Path.Combine(directory, "honorMissions.json"), """[{"id":10,"honorMissionType":"duplicate_card_total","requirement":20}]""");
        File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"), "[]");
        var random = new RarityRandom();
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userChargedCurrency = new() { free = 6000 };
        state.Data.userGachaTickets = [new() { gachaTicketId = 1, quantity = 10 }];
        state.Data.refreshableTypes = ["userMaterials"];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<IUserStore>(store).AddSingleton<Random>(random).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        UserGachaResponse Draw(int behavior = 1) => DumpSerializer.Deserialize<UserGachaResponse>(operation.Execute(1, () =>
        {
            var result = gacha.ExecuteGacha(1, behavior, false);
            result.updatedResources = user.BuildRefresh(prepareRefresh: false);
            return result;
        }));
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        random.Values = new(Enumerable.Repeat(0d, 11));
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            gacha.ExecuteGacha(1, 1, false);
            return new BrokenResponse();
        }), "十连完成扣发及全部联动后，响应编码失败整体回滚");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "十连失败保留货币、卡牌、天井、任务及待刷新字段");
        random.Values = new(Enumerable.Repeat(0d, 11));
        var first = Draw();
        Check.That(first.obtainPrizes.Length == 10 && first.obtainPrizes.Take(9).All(p => p.card.resourceId == 2) &&
            first.obtainPrizes[9].card.resourceId == 3 && random.Values.Count == 0, "全低星输入触发补选，十连最终至少一张三星");
        Check.That(first.obtainPrizes.Count(p => p.newFlg) == 2 && store.Read(1)!.Data.userCards.Single(c => c.cardId == 2).duplicateCount == 8,
            "同一次十连按抽取顺序判断首次与重复，同卡后续八张进入候场室");
        Check.That(first.userGacha.count == 1 && first.obtainGachaCeilItems.Single().quantity == 10 &&
            first.consumedCosts.Single().quantity == 3000 && store.Read(1)!.Data.userChargedCurrency.free == 3000,
            "十连只扣一次总费用，行为次数增加一次、天井增加十个");
        random.Values = new(new[] { .999 }.Concat(Enumerable.Repeat(0d, 9)));
        var second = Draw();
        Check.That(second.obtainPrizes[0].card.resourceId == 3 && second.obtainPrizes[9].card.resourceId == 2 && random.Values.Count == 0,
            "已出现三星时保留其位置和后续低星结果，不额外强制末位三星");
        Check.That(second.obtainPrizes.All(p => !p.newFlg) && second.userGacha.count == 2 && store.Read(1)!.Data.userChargedCurrency.free == 0,
            "连续第二次十连累计行为次数，已持有卡全为重复，免费余额扣到零");
        before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<GachaRequestRejectedException>(() => Draw(), "十连余额不足时拒绝");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "余额不足不产生第三次抽取或额外任务进度");
        random.Values = new(Enumerable.Repeat(0d, 11));
        var ticket = Draw(2);
        Check.That(ticket.consumedCosts.Single().quantity == 0 && store.Read(1)!.Data.userGachaTickets.Single().quantity == 0 &&
            store.Read(1)!.Data.userGachaCeilItems.Single().quantity == 30, "十张票券一次扣到零并得到十张卡和十个天井");
        state = store.Read(1)!;
        Check.That(state.Data.userCharacterMissions.Single(m => m.characterMissionType == "waiting_room").progress == 28 &&
            state.Data.userCharacterMissions.Single(m => m.characterMissionType == "collect_member").progress == 2 &&
            state.Data.userCharacterMissions.Single(m => m.characterMissionType == "collect_character_archive_voice").progress == 1 &&
            state.Data.userHonorMissions.Single().progress == 28 && state.Data.userMissionStatuses.Single().missionStatus == "achieved",
            "跨十连累计候场室与称号，首次卡牌和语音只计一次，称号达到master门槛");
        var path = Path.Combine(directory, "users.db");
        using (var sql = new SqliteUserStore(path)) sql.Save(1, state);
        using (var sql = new SqliteUserStore(path))
        {
            var restored = sql.Read(1)!.Data;
            // 待刷新标记不属于持久块。
            restored.refreshableTypes = state.Data.refreshableTypes;
            Check.That(DumpSerializer.Serialize(restored).SequenceEqual(DumpSerializer.Serialize(state.Data)), "数据库重启保留十连全部持久字段");
        }
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "十连及失败不修改其他账号");
    }

    private sealed class RarityRandom : Random
    {
        public Queue<double> Values { get; set; } = new();
        public override double NextDouble() => Values.Dequeue();
        public override long NextInt64(long maxValue) => 0;
    }
}
