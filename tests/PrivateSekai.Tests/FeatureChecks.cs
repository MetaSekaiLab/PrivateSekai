extern alias game;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Gacha;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Live;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Modules.Shop;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class FeatureChecks
{
    public static void Run()
    {
        var cache = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PinTables:0"] = "shopItems" })
            .Build().Get<MasterCacheConfig>()!;
        Check.That(cache.PinTables.SequenceEqual(["shopItems"]), "配置指定的预加载表不会合并隐式默认大表");
        Check.That(new MasterCacheConfig().PinTables.Length == 0, "默认不预加载大表");
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../obj/feature-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            WriteMaster(directory);
            var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
            var store = new MemoryUserStore();
            using var provider = new ServiceCollection().AddPrivateSekai()
                .AddSingleton(master).AddSingleton<IUserStore>(store)
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            ShopPurchase(provider, store);
            CardAndMission(provider, store);
            SpecialTraining(provider, store);
            LiveSettlement(provider, store);
            GachaDraw(provider, store);
            Console.WriteLine("业务：正式 DI 注册、商店、卡牌与任务联动、Live 结算、确定性抽卡检查通过。");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ShopPurchase(ServiceProvider provider, IUserStore store)
    {
        store.Save(1, TestUsers.Create(1));
        using var scope = provider.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var shop = scope.ServiceProvider.GetRequiredService<ShopService>();
        var bytes = operation.Execute(1, () =>
        {
            shop.PurchaseShopItem(1, 1);
            return user.BuildRefresh();
        });
        var refresh = DumpSerializer.Deserialize<SuiteUser>(bytes);
        Check.That(refresh.userGamedata.coin == 70 && refresh.userMaterials.Single().quantity == 4,
            "商店按 master 扣除金币并发放材料");
        Check.That(refresh.userShops.Single().userShopItems.Single().status == UserShopItem.STATUS_SOLD_OUT,
            "商店商品状态进入同次刷新");
        Check.That(store.Read(1)!.Data.userGamedata.coin == 70 && store.Read(1)!.Data.userMaterials.Single().quantity == 4,
            "商店收支共同提交");
    }

    private static void CardAndMission(ServiceProvider provider, IUserStore store)
    {
        var state = TestUsers.Create(2);
        state.Data.userCards = [new UserCard { userId = 2, cardId = 1, level = 1, duplicateCount = 2 }];
        state.Data.userPracticeTickets = [new UserPracticeTicket { userId = 2, practiceTicketId = 1, quantity = 2 }];
        store.Save(2, state);
        using var scope = provider.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();
        var missions = scope.ServiceProvider.GetRequiredService<MissionService>();
        var practiceBytes = operation.Execute(2, () =>
        {
            var response = cards.PracticeCardWithTickets(1,
                [new UserResource { resourceType = "practice_ticket", resourceId = 1, quantity = 1 }]);
            response.updatedResources = user.BuildRefresh();
            return response;
        });
        var practice = DumpSerializer.Deserialize<UserCardPracticeTicketResponse>(practiceBytes);
        Check.That(practice.updateExpResult.beforeLevel == 1 && practice.updateExpResult.afterLevel == 2 &&
            practice.updatedResources.userPracticeTickets.Single().quantity == 1, "卡牌练习消耗与经验变化一致");
        Check.That(practice.updatedResources.userCards.Single().duplicateCount == 2, "练习已有卡不改变重复卡数量");
        Check.That(practice.updatedResources.userBeginnerMissionV2s.Single().beginnerMissionV2Id == 6 &&
            practice.updatedResources.userMissionStatuses.Single().missionStatus == "achieved", "卡牌升级联动任务并合并变化");

        var missionBytes = operation.Execute(2, () =>
        {
            var response = missions.ReceiveBeginnerMissionV2Rewards([6]);
            response.UpdatedResources = user.BuildRefresh();
            return response;
        });
        var reward = DumpSerializer.Deserialize<UserMissionReceiveResponse>(missionBytes);
        Check.That(reward.ObtainedRewards.Single().quantity == 15 && reward.UpdatedResources.userGamedata.coin == 115,
            "任务奖励通过资源分派发放");
        Check.That(reward.UpdatedResources.userMissionStatuses.Single().missionStatus == "received", "任务领取状态提交");
    }

    private static void LiveSettlement(ServiceProvider provider, IUserStore store)
    {
        var state = TestUsers.Create(3);
        state.Data.userGamedata.rank = 1;
        state.Data.userCards = [new() { cardId = 1, level = 1 }];
        state.Data.userDecks = [new() { deckId = 1, member1 = 1, leader = 1 }];
        state.Data.userBoost = new() { current = 3 };
        state.Data.userEventBreakTime = new() { lastDecreaseAt = 0 };
        store.Save(3, state);
        using var scope = provider.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var live = scope.ServiceProvider.GetRequiredService<LiveService>();
        var started = operation.Execute(3, () => live.StartUserLive(new UserLiveRequest
        {
            musicId = 7, musicDifficultyId = 71, deckId = 1, boostCount = 1
        }));
        var liveId = DumpSerializer.Deserialize<UserLive>(started).userLiveId;
        Check.That(store.Read(3)!.Private.UserLiveSessions.ContainsKey(liveId), "开始 Live 保存私有会话");
        var clearRequest = new UserLiveClearRequest
        {
            score = 150, perfectCount = 10, maxCombo = 10, life = 1000,
            ingameCutinCharacterArchiveVoiceGroupIds = [4]
        };
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(3, () =>
        {
            live.ClearUserLive(liveId, clearRequest);
            return new BrokenResponse();
        }), "Live 结算序列化失败");
        var rolledBack = store.Read(3)!;
        Check.That(rolledBack.Private.UserLiveSessions.ContainsKey(liveId) && rolledBack.Data.userBoost.current == 3 &&
            rolledBack.Data.userMaterials.Length == 0 && rolledBack.Data.userGamedata.coin == 100 &&
            rolledBack.Data.userGamedata.totalExp == 0 && rolledBack.Data.userCards.Single().totalExp == 0,
            "Live 失败恢复会话、体力及奖励");

        var cleared = operation.Execute(3, () =>
        {
            var response = live.ClearUserLive(liveId, clearRequest);
            response.updatedResources = user.BuildRefresh();
            return response;
        });
        var result = DumpSerializer.Deserialize<UserLiveClearResponse>(cleared);
        Check.That(result.fullPerfectFlg && result.highScoreFlg && result.updatedResources.userMusicResults.Single().highScore == 150,
            "Live 结算保存成绩与判定结果");
        Check.That(result.updatedResources.userMaterials.Single().quantity == 2 && result.updatedResources.userGamedata.coin == 105,
            "Live 发放倍率奖励与首次成就奖励");
        Check.That(result.updatedResources.userBoost.current == 12 && result.updatedResources.userLiveMissions.Single().progress == 3,
            "Live 体力消耗与任务进度合并刷新");
        Check.That(result.userExpResult.afterTotalExp == 200 && result.userExpResult.afterLevel == 2 &&
            result.playerRankRewards.Single().quantity == 50 && result.updatedResources.userChargedCurrency.free == 50,
            "演出玩家经验跨级并通过 master 发放等级奖励");
        Check.That(result.deckCardExpResults.Single().expResult.afterTotalExp == 300 &&
            result.updatedResources.userCards.Single().level == 3,
            "演出卡牌经验使用卡牌等级上限");
        Check.That(!store.Read(3)!.Private.UserLiveSessions.ContainsKey(liveId), "Live 成功后移除会话");
        operation.Execute(3, () => live.ClearUserLive(liveId, clearRequest));
        Check.That(store.Read(3)!.Data.userGamedata.coin == 105 && store.Read(3)!.Data.userLiveMissions.Single().progress == 3 &&
            store.Read(3)!.Data.userGamedata.totalExp == 200 && store.Read(3)!.Data.userChargedCurrency.free == 50,
            "重复提交结束的 Live 不重复发奖或累计任务");
        var failedStart = operation.Execute(3, () => live.StartUserLive(new UserLiveRequest
        {
            musicId = 7, musicDifficultyId = 71, deckId = 1, boostCount = 1
        }));
        var failedId = DumpSerializer.Deserialize<UserLive>(failedStart).userLiveId;
        var failedBytes = operation.Execute(3, () =>
        {
            var response = live.ClearUserLive(failedId, new UserLiveClearRequest
            {
                score = 120, perfectCount = 8, missCount = 2, maxCombo = 8, life = 0
            });
            response.updatedResources = user.BuildRefresh();
            return response;
        });
        var failed = DumpSerializer.Deserialize<UserLiveClearResponse>(failedBytes);
        Check.That(failed.userExpResult.beforeTotalExp == 200 && failed.userExpResult.afterTotalExp == 400 &&
            failed.deckCardExpResults.Single().expResult.afterTotalExp == 300,
            "C 档失败仍发经验，满级卡保持上限");
        Check.That(!failed.fullComboFlg && !failed.fullPerfectFlg && !failed.highScoreFlg &&
            failed.updatedResources.userMusicResults == null &&
            store.Read(3)!.Data.userMusicResults.Single().playResult == "full_perfect" &&
            store.Read(3)!.Data.userMusicResults.Single().highScore == 150,
            "失败结算保留已有最佳成绩，未变化成绩不重复刷新");
        Check.Throws<ArgumentException>(() => operation.Execute(3, () => live.StartUserLive(new UserLiveRequest
        {
            musicId = 7, musicDifficultyId = 71, deckId = 1, isAuto = true, boostCount = 0
        })), "普通 Auto 不接受零体力消耗");
        var autoStart = operation.Execute(3, () => live.StartUserLive(new UserLiveRequest
        {
            musicId = 7, musicDifficultyId = 71, deckId = 1, isAuto = true, boostCount = 1
        }));
        var autoId = DumpSerializer.Deserialize<UserLive>(autoStart).userLiveId;
        Check.That((store.Read(3)!.Data.userAutoLive?.count ?? 0) == 0 && store.Read(3)!.Data.userBoost.current == 11,
            "Auto 开局不提前累计次数或扣体力");
        var autoRequest = new UserLiveClearRequest { score = 200, life = 1000 };
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(3, () =>
        {
            live.ClearUserLive(autoId, autoRequest);
            return new BrokenResponse();
        }), "Auto 结算编码失败回滚");
        Check.That((store.Read(3)!.Data.userAutoLive?.count ?? 0) == 0 &&
            store.Read(3)!.Data.userBoost.current == 11 && store.Read(3)!.Data.userGamedata.totalExp == 400 &&
            store.Read(3)!.Private.UserLiveSessions.ContainsKey(autoId), "Auto 失败回滚次数、经验、体力和会话");
        var autoBytes = operation.Execute(3, () =>
        {
            var response = live.ClearUserLive(autoId, autoRequest);
            response.updatedResources = user.BuildRefresh();
            return response;
        });
        var auto = DumpSerializer.Deserialize<UserLiveClearResponse>(autoBytes);
        Check.That(!auto.fullComboFlg && !auto.fullPerfectFlg && !auto.highScoreFlg &&
            store.Read(3)!.Data.userMusicResults.Single().highScore == 150,
            "Auto 的空判定不视为 FC/AP，也不刷新更高成绩");
        Check.That(auto.userExpResult.afterTotalExp == 600 && auto.updatedResources.userAutoLive.count == 1 &&
            auto.updatedResources.userBoost.current == 10, "Auto C 档经验、次数与体力在结算中提交");
        operation.Execute(3, () => live.ClearUserLive(autoId, autoRequest));
        Check.That(store.Read(3)!.Data.userAutoLive.count == 1 && store.Read(3)!.Data.userGamedata.totalExp == 600 &&
            store.Read(3)!.Data.userBoost.current == 10, "重复 Auto 结算不重复计数、发经验或扣体力");
    }

    private static void SpecialTraining(ServiceProvider provider, IUserStore store)
    {
        var state = TestUsers.Create(5);
        state.Data.userCards = [new UserCard { cardId = 2, level = 40, totalExp = 300, defaultImage = "original" }];
        state.Data.userMaterials = [new UserMaterial { materialId = 10, quantity = 100 },
            new UserMaterial { materialId = 14, quantity = 49 }];
        store.Save(5, state);
        using var scope = provider.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();
        Check.Throws<ArgumentException>(() => operation.Execute(5, () =>
        {
            cards.SetCardSpecialTrainingStatus(2, "done");
            return user.BuildRefresh();
        }), "特训材料不足拒绝执行");
        Check.That(store.Read(5)!.Data.userMaterials[0].quantity == 100 &&
            store.Read(5)!.Data.userCards.Single().specialTrainingStatus != "done", "特训失败不扣其他材料");
        state.Data.userMaterials[1].quantity = 50;
        state.Data.userCards[0].level = 39;
        store.Save(5, state);
        Check.Throws<ArgumentException>(() => operation.Execute(5, () =>
        {
            cards.SetCardSpecialTrainingStatus(2, "done");
            return user.BuildRefresh();
        }), "特训要求达到未特训等级上限");
        state.Data.userCards[0].level = 40;
        store.Save(5, state);
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(5, () =>
        {
            cards.SetCardSpecialTrainingStatus(2, "done");
            return new BrokenResponse();
        }), "特训编码失败回滚");
        Check.That(store.Read(5)!.Data.userCards.Single().specialTrainingStatus != "done" &&
            store.Read(5)!.Data.userCostume3dStatuses == null, "回滚特训状态及特殊服装奖励");
        var bytes = operation.Execute(5, () =>
        {
            cards.SetCardSpecialTrainingStatus(2, "done");
            return user.BuildRefresh();
        });
        var result = DumpSerializer.Deserialize<SuiteUser>(bytes);
        Check.That(result.userCards.Single().specialTrainingStatus == "done" &&
            result.userCards.Single().level == 40 && result.userCards.Single().totalExp == 300 &&
            result.userMaterials.All(m => m.quantity == 0) && result.userCostume3dStatuses.Single().costume3dId == 721001,
            "特训扣除 master 材料并发放配置奖励，保留等级经验");
        Check.That(scope.ServiceProvider.GetRequiredService<CardMasterQueries>().GetCardMaxLevel(2, true) == 50,
            "特训后练习使用新的等级上限");
        operation.Execute(5, () =>
        {
            cards.SetCardSpecialTrainingStatus(2, "done");
            return user.BuildRefresh();
        });
        Check.That(store.Read(5)!.Data.userCostume3dStatuses.Length == 1 &&
            store.Read(5)!.Data.userMaterials.All(m => m.quantity == 0), "重复特训不重复扣材及发奖");
    }

    private static void GachaDraw(ServiceProvider provider, IUserStore store)
    {
        var state = TestUsers.Create(4);
        state.Data.userChargedCurrency = new ChargedCurrency { free = 700, paid = 100, paidUnitPrices = [] };
        store.Save(4, state);
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var user = services.GetRequiredService<UserSession>();
        var operation = services.GetRequiredService<UserOperation>();

        // 缺少天井道具处理器，在扣费、授卡和服装发放之后触发失败。
        var incompleteResources = new ResourceService(user,
            services.GetServices<IResourceHandler>().Where(handler => handler is not GachaResourceHandler));
        var incompleteGacha = new GachaService(user,
            services.GetRequiredService<GachaMasterQueries>(),
            services.GetRequiredService<CardMasterQueries>(),
            services.GetRequiredService<ResourceMasterQueries>(), incompleteResources);
        var failure = Check.Throws<NotSupportedException>(
            () => operation.Execute(4, () => incompleteGacha.ExecuteGacha(1, 1, false)), "抽卡中途资源处理失败");
        Check.That(failure.Message.Contains("gacha_ceil_item", StringComparison.Ordinal), "抽卡失败发生在末尾天井道具发放阶段");
        var rolledBack = store.Read(4)!.Data;
        Check.That(rolledBack.userChargedCurrency.free == 700 && rolledBack.userChargedCurrency.paid == 100 &&
            rolledBack.userCards.Length == 0 && rolledBack.userCostume3dStatuses == null && rolledBack.userGachas == null,
            "抽卡中途失败回滚费用、卡牌、服装和抽卡记录");

        var gacha = services.GetRequiredService<GachaService>();
        var bytes = operation.Execute(4, () =>
        {
            var response = gacha.ExecuteGacha(1, 1, false);
            response.updatedResources = user.BuildRefresh();
            return response;
        });
        var result = DumpSerializer.Deserialize<UserGachaResponse>(bytes);
        Check.That(result.obtainPrizes.Select(prize => prize.card.resourceId).SequenceEqual([10, 10]) &&
            result.obtainPrizes.Select(prize => prize.newFlg).SequenceEqual([true, false]),
            "单卡池双抽稳定返回首次新卡与重复卡标记");
        Check.That(result.updatedResources.userCards.Single().duplicateCount == 1,
            "双抽同卡只建立一张卡并累计一个重复数量");
        Check.That(result.obtainPrizes[0].costume3d.Single().resourceId == 20 &&
            result.obtainPrizes[1].costume3d.Length == 0 && result.updatedResources.userCostume3dStatuses.Length == 1,
            "抽卡服装仅在首次授卡时发放");
        Check.That(result.consumedCosts.Single().quantity == 500 &&
            result.updatedResources.userChargedCurrency.free == 400 && result.updatedResources.userChargedCurrency.paid == 100,
            "抽卡成本响应保留剩余余额语义并优先扣免费水晶");
        Check.That(result.obtainGachaCeilItems.Single().resourceId == 9 &&
            result.obtainGachaCeilItems.Single().quantity == 2 && result.updatedResources.userGachaCeilItems.Single().quantity == 2,
            "双抽天井道具按抽取次数发放");
        var saved = store.Read(4)!.Data;
        Check.That(saved.userGachas.Single().count == 1 && saved.userCards.Single().duplicateCount == 1 &&
            saved.userChargedCurrency.free == 400, "成功抽卡统一提交记录、卡牌与扣费结果");
    }

    private static void WriteMaster(string directory)
    {
        var tables = new Dictionary<string, string>
        {
            ["shopItems"] = """[{"id":1,"shopId":1,"resourceBoxId":10,"costs":[{"cost":{"resourceType":"coin","resourceId":0,"quantity":30}}]}]""",
            ["resourceBoxes"] = """
                [
                  {"id":10,"resourceBoxPurpose":"shop_item","details":[{"resourceType":"material","resourceId":1,"resourceQuantity":4}]},
                  {"id":20,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"coin","resourceId":0,"resourceQuantity":15}]},
                  {"id":62,"resourceBoxPurpose":"score_rank_reward_detail","details":[{"resourceType":"material","resourceId":2,"resourceQuantity":1}]},
                  {"id":80,"resourceBoxPurpose":"music_achievement","details":[{"resourceType":"coin","resourceId":0,"resourceQuantity":5}]},
                  {"id":1,"resourceBoxPurpose":"player_rank_reward","details":[{"resourceType":"jewel","resourceQuantity":50}]},
                  {"id":90,"resourceBoxPurpose":"special_training_reward","details":[{"resourceType":"costume_3d","resourceId":721001,"resourceQuantity":1}]}
                ]
                """,
            ["cards"] = """
                [{"id":1,"cardRarityType":"rarity_1"},
                 {"id":2,"cardRarityType":"rarity_3","specialTrainingPower1BonusFixed":100,
                  "specialTrainingRewardResourceBoxId":90,
                  "specialTrainingCosts":[{"cardId":2,"cost":{"resourceType":"material","resourceId":10,"quantity":100}},
                                          {"cardId":2,"cost":{"resourceType":"material","resourceId":14,"quantity":50}}]}]
                """,
            ["cardEpisodes"] = "[]",
            ["cardCostume3ds"] = """[{"cardId":10,"costume3dId":20}]""",
            ["gachas"] = """
                [{"id":1,"gachaCeilItemId":9,"gachaDetails":[{"cardId":10,"weight":1}],
                  "gachaBehaviors":[{"id":1,"gachaBehaviorType":"normal","spinCount":2,"costResourceType":"jewel","costResourceQuantity":300}]}]
                """,
            ["cardRarities"] = """[{"cardRarityType":"rarity_1","maxLevel":3,"trainingMaxLevel":3},{"cardRarityType":"rarity_3","maxLevel":40,"trainingMaxLevel":50}]""",
            ["practiceTickets"] = """[{"id":1,"exp":100}]""",
            ["levels"] = """[{"levelType":"card","level":1,"totalExp":0},{"levelType":"card","level":2,"totalExp":100},{"levelType":"card","level":3,"totalExp":300},{"levelType":"user","level":1,"totalExp":0},{"levelType":"user","level":2,"totalExp":100},{"levelType":"user","level":3,"totalExp":1000}]""",
            ["playerRankRewards"] = """[{"playerRank":2,"seq":1,"resourceBoxId":1}]""",
            ["configs"] = """[{"configKey":"rank_up_recover_boost_count","value":"10"}]""",
            ["beginnerMissionV2s"] = """[{"id":6,"beginnerMissionV2Type":"any_card_level_up","requirement":1,"rewards":[{"resourceBoxId":20}]}]""",
            ["musicDifficulties"] = """[{"id":71,"musicId":7,"musicDifficulty":"easy","playLevel":6,"totalNoteCount":10}]""",
            ["playLevelScores"] = """[{"liveType":"solo","playLevel":6,"s":500,"a":400,"b":300,"c":100}]""",
            ["boosts"] = """[{"id":1,"costBoost":1,"expRate":1,"rewardRate":2,"livePointRate":3}]""",
            ["liveMissionPasses"] = """[{"id":1,"liveMissionPeriodId":1}]""",
            ["musicAchievements"] = """[{"id":1,"musicAchievementType":"score_rank","musicAchievementTypeValue":"rank_c","resourceBoxId":80}]"""
        };
        foreach (var (table, json) in tables)
            File.WriteAllText(Path.Combine(directory, table + ".json"), json);
    }
}
