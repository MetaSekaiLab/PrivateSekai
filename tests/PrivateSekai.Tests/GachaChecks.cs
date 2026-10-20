extern alias game;

using System;
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

internal static class GachaChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "gacha-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "gachas.json"), """
            [{"id":1,"startAt":900,"endAt":1100,"gachaCeilItemId":9,"gachaDetails":[{"cardId":2,"weight":1}],
              "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100}],
              "gachaBehaviors":[{"id":1,"gachaBehaviorType":"normal","spinCount":1,"resourceCategory":"consume_resource","costResourceType":"jewel","costResourceQuantity":100},
                {"id":2,"gachaBehaviorType":"normal","spinCount":1,"resourceCategory":"consume_resource","costResourceType":"gacha_ticket","costResourceId":1,"costResourceQuantity":1},
                {"id":3,"gachaBehaviorType":"normal","spinCount":1,"resourceCategory":"consume_resource","costResourceType":"paid_jewel","costResourceQuantity":100},
                {"id":4,"gachaBehaviorType":"normal","spinCount":1,"resourceCategory":"consume_resource","costResourceType":"unsupported","costResourceQuantity":100},
                {"id":5,"gachaBehaviorType":"once_a_week","spinCount":1,"resourceCategory":"free_resource","gachaSpinnableType":"colorful_pass"}]},
             {"id":2,"startAt":900,"endAt":1100,"gachaDetails":[],"gachaBehaviors":[{"id":6,"gachaBehaviorType":"normal","spinCount":1,"costResourceType":"jewel","costResourceQuantity":100}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "cards.json"), """[{"id":2,"characterId":2,"cardRarityType":"rarity_2"}]""");
        File.WriteAllText(Path.Combine(directory, "cardEpisodes.json"), """[{"id":20,"cardId":2},{"id":21,"cardId":2}]""");
        File.WriteAllText(Path.Combine(directory, "cardCostume3ds.json"), "[]");
        File.WriteAllText(Path.Combine(directory, "cardExtras.json"), "[]");
        File.WriteAllText(Path.Combine(directory, "characterMissionV2s.json"), """
            [{"id":2,"characterId":2,"characterMissionType":"collect_member","parameterGroupId":1},
             {"id":3,"characterId":2,"characterMissionType":"waiting_room","parameterGroupId":1},
             {"id":4,"characterId":2,"characterMissionType":"collect_character_archive_voice","parameterGroupId":1}]
            """);
        File.WriteAllText(Path.Combine(directory, "characterArchiveVoices.json"), """
            [{"id":1,"groupId":1,"gameCharacterId":2,"characterArchiveVoiceType":"collect_card","externalId":2}]
            """);
        File.WriteAllText(Path.Combine(directory, "characterMissionV2ParameterGroups.json"), """[{"id":1,"seq":1,"requirement":1}]""");
        File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"), "[]");
        File.WriteAllText(Path.Combine(directory, "honorMissions.json"), """[{"id":10,"honorMissionType":"duplicate_card_total","requirement":2}]""");
        File.WriteAllText(Path.Combine(directory, "panelMissionCampaigns.json"), """[{"id":1,"startAt":900,"progressEndAt":1100}]""");
        var clock = new Clock { Now = 1000 };
        var store = new MemoryUserStore();
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<TimeProvider>(clock).AddSingleton<IUserStore>(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        var state = TestUsers.Create(1);
        state.Data.userCards = [];
        state.Data.userChargedCurrency = new() { free = 150, paid = 100 };
        state.Data.userMissionStatuses = [];
        state.Data.userCharacterMissions = [];
        state.Data.userCharacterMissionStatuses = [];
        state.Data.userHonorMissions = [];
        state.Data.userPanelMissions = [];
        state.Data.userPanelMissionSheets = [];
        state.Data.refreshableTypes = ["userMaterials"];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        UserGachaResponse Draw(int id = 1, int behavior = 1, bool paidFirst = false) =>
            DumpSerializer.Deserialize<UserGachaResponse>(operation.Execute(1, () =>
            {
                var response = gacha.ExecuteGacha(id, behavior, paidFirst);
                response.updatedResources = user.BuildRefresh();
                return response;
            }));
        void Reject(int id, int behavior, int status, string code)
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            var error = Check.Throws<GachaRequestRejectedException>(() => Draw(id, behavior), "不可执行的抽卡请求拒绝");
            Check.That(error.Status == status && error.Code == code && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "抽卡拒绝返回明确分类，不扣款、不发卡、不改次数任务或消费待刷新字段");
        }
        Reject(int.MaxValue, 1, 404, "");
        Reject(1, int.MaxValue, 404, "");
        Reject(1, 2, 409, "not_enough_resource");
        Reject(1, 5, 409, "");
        clock.Now = 899;
        Reject(1, 1, 409, "invalid_term");
        clock.Now = 1100;
        Reject(1, 1, 409, "invalid_term");
        clock.Now = 1000;
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<NotSupportedException>(() => Draw(1, 4), "未知消耗类型不能当免费抽卡");
        Check.Throws<InvalidOperationException>(() => Draw(2, 6), "缺少概率与卡池配置不能从全卡列表随机补卡");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "错误master配置在已进入扣款后失败也整体回滚");
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            gacha.ExecuteGacha(1, 1, false);
            return new BrokenResponse();
        }), "抽卡结果编码失败回滚全部扣发");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败不留下卡牌、天井、角色收集任务或抽取次数");
        clock.Now = 900;
        var first = Draw();
        Check.That(first.consumedCosts.Single().quantity == 150 && first.obtainPrizes.Single().newFlg &&
            first.updatedResources.userChargedCurrency.free == 50 && first.updatedResources.userChargedCurrency.paid == 100,
            "普通单抽在起点时刻可执行，免费优先且消耗响应保持剩余总余额语义");
        Check.That(first.updatedResources.userCards.Single().episodes.Select(e => e.cardEpisodeId).SequenceEqual([20, 21]) &&
            store.Read(1)!.Data.userCharacterMissions.Single(m => m.characterMissionType == "collect_member").progress == 1 &&
            store.Read(1)!.Data.userCharacterMissions.Single(m => m.characterMissionType == "collect_character_archive_voice").progress == 1 &&
            store.Read(1)!.Data.userCharacterMissionStatuses.Count(s => s.missionStatus == "achieved") == 2 &&
            first.updatedResources.userPanelMissions.Length == 0,
            "首次发卡保存剧情、推进收集及master关联语音任务，活动期未选面板返回空刷新");
        var second = Draw(paidFirst: true);
        Check.That(!second.obtainPrizes.Single().newFlg && second.updatedResources.userCards.Single().duplicateCount == 1 &&
            second.updatedResources.userChargedCurrency.free == 50 && second.updatedResources.userChargedCurrency.paid == 0 &&
            second.consumedCosts.Single().quantity == 50 && second.userGacha.count == 2,
            "再次单抽按付费优先扣款，重复卡进入重复数，抽取次数独立累计");
        Check.That(store.Read(1)!.Data.userCharacterMissions.Single(m => m.characterMissionType == "collect_member").progress == 1 &&
            store.Read(1)!.Data.userCharacterMissions.Single(m => m.characterMissionType == "collect_character_archive_voice").progress == 1 &&
            store.Read(1)!.Data.userCharacterMissions.Single(m => m.characterMissionType == "waiting_room").progress == 1 &&
            store.Read(1)!.Data.userHonorMissions.Single().progress == 1 && store.Read(1)!.Data.userGachaCeilItems.Single().quantity == 2,
            "重复卡推进候场室及重复称号，不重复计算卡牌和语音收集，天井按实际抽数增加");
        Reject(1, 1, 409, "not_enough_resource");
        Reject(1, 3, 409, "not_enough_resource");
        state = store.Read(1)!;
        state.Data.userGachaTickets = [new() { userId = 1, gachaTicketId = 1, quantity = 1 }];
        store.Save(1, state);
        Check.That(Draw(1, 2).consumedCosts.Single().quantity == 0 && store.Read(1)!.Data.userGachaTickets.Single().quantity == 0 &&
            store.Read(1)!.Data.userGachaCeilItems.Single().quantity == 3, "票券刚好足够时扣到零并发卡和天井");
        Reject(1, 2, 409, "not_enough_resource");
        var path = Path.Combine(directory, "users.db");
        using (var sql = new SqliteUserStore(path)) sql.Save(1, store.Read(1)!);
        using (var sql = new SqliteUserStore(path))
            Check.That(sql.Read(1)!.Data.userCards.Single().duplicateCount == 2 && sql.Read(1)!.Data.userGachaCeilItems.Single().quantity == 3 &&
                sql.Read(1)!.Data.userCharacterMissions.Single(m => m.characterMissionType == "collect_member").progress == 1 &&
                sql.Read(1)!.Data.userHonorMissions.Single().progress == 2 &&
                sql.Read(1)!.Data.userMissionStatuses.Single(s => s.missionType == "honor_mission" && s.missionId == 10).missionStatus == "achieved",
                "数据库重启保留重复卡、天井、收集及重复称号达到门槛的状态");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "单抽与失败不改变其他账号");
    }

    private sealed class Clock : TimeProvider
    {
        public long Now { get; set; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
}
