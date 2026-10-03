extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Live;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class ChallengeStageChecks
{
    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/challenge-stage-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "liveMissionPeriods.json"), """
            [{"id":99,"startAt":100,"endAt":200},
             {"id":9,"startAt":300,"endAt":4102444800000},
             {"id":100,"startAt":4102444800000,"endAt":4105123200000}]
            """);
        File.WriteAllText(Path.Combine(directory, "configs.json"), """
            [{"configKey":"challenge_base_point","value":"17"},
             {"configKey":"challenge_point_calc_value","value":"13"},
             {"configKey":"obtain_live_point_for_challenge_live","value":"30"},
             {"configKey":"challenge_live_limited_term_score_rank_reward_rate","value":"7"}]
            """);
        File.WriteAllText(Path.Combine(directory, "birthdayParties.json"), """
            [{"id":1,"startAt":100,"birthdayStartAt":150,"closedAt":200,"deliveryItemMaterialId":601},
             {"id":2,"startAt":300,"birthdayStartAt":4100000000000,"closedAt":4102444800000,"deliveryItemMaterialId":602}]
            """);
        File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"), """
            [{"id":71,"beginnerMissionV2Type":"challenge_live_clear","requirement":1},
             {"id":72,"beginnerMissionV2Type":"any_live_clear","requirement":3}]
            """);
        File.WriteAllText(Path.Combine(directory, "liveMissions.json"), """
            [{"id":81,"liveMissionPeriodId":9,"liveMissionType":"free","requirement":30}]
            """);
        File.WriteAllText(Path.Combine(directory, "characterMissionV2s.json"), """
            [{"id":91,"characterId":1,"characterMissionType":"play_live","parameterGroupId":1}]
            """);
        File.WriteAllText(Path.Combine(directory, "characterMissionV2ParameterGroups.json"), """
            [{"id":1,"seq":1,"requirement":10}]
            """);
        File.WriteAllText(Path.Combine(directory, "musicDifficulties.json"), """
            [{"id":1,"playLevel":5},{"id":2,"playLevel":6}]
            """);
        File.WriteAllText(Path.Combine(directory, "playLevelScores.json"), """
            [{"liveType":"solo","playLevel":5,"c":10,"b":20,"a":30,"s":40},
             {"liveType":"challenge_live","playLevel":5,"c":100,"b":200,"a":300,"s":400}]
            """);
        File.WriteAllText(Path.Combine(directory, "levels.json"), """
            [{"levelType":"character","level":1,"totalExp":0},
             {"levelType":"character","level":2,"totalExp":1},
             {"levelType":"character","level":3,"totalExp":3},
             {"levelType":"character","level":4,"totalExp":6},
             {"levelType":"character","level":5,"totalExp":10}]
            """);
        File.WriteAllText(Path.Combine(directory, "characterRanks.json"), """
            [{"characterId":1,"characterRank":2,"rewardResourceBoxIds":[8]},
             {"characterId":1,"characterRank":3,"rewardResourceBoxIds":[9]},
             {"characterId":1,"characterRank":4,"rewardResourceBoxIds":[],"characterRankAchieveResources":[{"releaseConditionId":1}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "challengeLiveStages.json"), """
            [{"id":101,"characterId":1,"rank":1,"nextStageChallengePoint":50,"completeStageResourceBoxId":7,"completeStageCharacterExp":1},
             {"id":102,"characterId":1,"rank":2,"nextStageChallengePoint":100,"completeStageResourceBoxId":7,"completeStageCharacterExp":2},
             {"id":103,"characterId":1,"rank":3,"nextStageChallengePoint":200,"completeStageResourceBoxId":7,"completeStageCharacterExp":3}]
            """);
        File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
            [{"id":7,"resourceBoxPurpose":"challenge_live_high_score","details":[{"resourceType":"jewel","resourceQuantity":999}]},
             {"id":7,"resourceBoxPurpose":"challenge_live_stage","details":[{"resourceType":"jewel","resourceQuantity":50}]},
             {"id":8,"resourceBoxPurpose":"character_rank_reward","details":[{"resourceType":"material","resourceId":18,"resourceQuantity":2}]},
             {"id":9,"resourceBoxPurpose":"character_rank_reward","details":[{"resourceType":"jewel","resourceQuantity":100}]}]
            """);
        var store = new MemoryUserStore();
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton<IUserStore>(store)
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory)).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var challenges = scope.ServiceProvider.GetRequiredService<ChallengeLiveService>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var master = scope.ServiceProvider.GetRequiredService<LiveMasterQueries>();
        var missionMaster = scope.ServiceProvider.GetRequiredService<MissionMasterQueries>();
        Check.That(master.GetBirthdayParty(99) == null && master.GetBirthdayParty(100)?.id == 1 &&
            master.GetBirthdayParty(199)?.id == 1 && master.GetBirthdayParty(200) == null &&
            master.GetBirthdayParty(300)?.deliveryItemMaterialId == 602, "生日活动按开始与关闭时间选择，不等到生日当天");
        foreach (var (time, period) in new[] { (99L, 0), (100L, 99), (199L, 99), (200L, 0), (299L, 0),
                     (300L, 9), (4102444799999L, 9), (4102444800000L, 100), (4105123200000L, 0) })
            Check.That(missionMaster.GetLiveMissionPeriodId(time) == period,
                "任务周期按时间包含起点、排除终点，不按 ID 大小或未来周期选择");
        foreach (var (score, expected) in new[] { (0, 17), (12, 17), (13, 18), (25, 18), (26, 19) })
            Check.That(master.CalculateChallengeBasePoint(score) == expected, "挑战点数读取配置并向下取整");
        Check.Throws<ArgumentOutOfRangeException>(() => master.CalculateChallengeBasePoint(-1), "拒绝负挑战分数");
        foreach (var (score, expected) in new[]
                 { (0, "rank_d"), (99, "rank_d"), (100, "rank_c"), (199, "rank_c"),
                   (200, "rank_b"), (299, "rank_b"), (300, "rank_a"), (399, "rank_a"), (400, "rank_s") })
            Check.That(master.GetChallengeScoreRank(1, score) == expected, "挑战评分使用独立表并包含门槛值");
        var missingThresholdRejected = false;
        try { master.GetChallengeScoreRank(2, 100); }
        catch (InvalidOperationException) { missingThresholdRejected = true; }
        Check.That(missingThresholdRejected, "挑战缺少评分表时不回退到普通演出门槛");
        void Reset()
        {
            var state = TestUsers.Create(1);
            state.Data.userCharacters = [new() { characterId = 1, characterRank = 1 }];
            store.Save(1, state);
        }
        void Advance(int point) => operations.Execute(1, () =>
        {
            challenges.AdvanceStage(1, point);
            return user.BuildRefresh();
        });
        Reset();
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            challenges.GrantBirthdayRewards(new(), new() { life = 1000 }, 300);
            return new BrokenResponse();
        }), "生日奖励编码失败");
        Check.That(store.Read(1)!.Data.userMaterials.Length == 0, "编码失败回滚生日材料");
        Check.Throws<NotSupportedException>(() => operations.Execute(1, () =>
            challenges.GrantBirthdayRewards(new(), new() { life = 1000 }, 100)), "跨生日活动不猜测奖励归属");
        operations.Execute(1, () =>
        {
            var rewards = challenges.GrantBirthdayRewards(new(), new() { life = 1000 }, 300);
            Check.That(rewards.Single().scoreRankRewardType == "birthday" &&
                rewards.Single().obtainedRewards.Single().resourceId == 602 &&
                rewards.Single().obtainedRewards.Single().quantity == 7, "生日材料与挑战数量均读取 master");
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userMaterials.Single().materialId == 602 &&
            store.Read(1)!.Data.userMaterials.Single().quantity == 7, "生日奖励写入材料库存");
        Reset();
        var playing = store.Read(1)!;
        playing.Private.ChallengeLiveSessions["session"] = new UserChallengeLiveStartRequest { characterId = 1, musicId = 1 };
        playing.Data.userChallengeLivePlayStatuses = [new() { userChallengeLiveId = "session", characterId = 1,
            musicId = 1, liveStatus = "start", playStartAt = 1 }];
        store.Save(1, playing);
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            challenges.CompletePlay("session", new UserChallengeLiveClearRequest { life = 1000 });
            return new BrokenResponse();
        }), "挑战完成状态编码失败");
        Check.That(store.Read(1)!.Private.ChallengeLiveSessions.ContainsKey("session") &&
            store.Read(1)!.Data.userChallengeLivePlayStatuses.Single().liveStatus == "start" &&
            store.Read(1)!.Data.userCharacterMissions?.Any() != true,
            "编码失败回滚会话移除、次数与角色任务");
        operations.Execute(1, () =>
        {
            Check.That(!challenges.CompletePlay("stale", new UserChallengeLiveClearRequest { life = 1000 }),
                "未知挑战会话不完成演出");
            Check.That(challenges.CompletePlay("session", new UserChallengeLiveClearRequest { life = 1000 }),
                "完成当前挑战会话");
            return user.BuildRefresh();
        });
        var completed = store.Read(1)!;
        Check.That(completed.Private.ChallengeLiveSessions.Count == 0 &&
            completed.Data.userChallengeLivePlayStatuses.Single().playCount == 1 &&
            completed.Data.userChallengeLivePlayStatuses.Single().liveStatus == "cleared" &&
            completed.Data.userChallengeLivePlayStatuses.Single().playEndAt > 1 &&
            completed.Data.userCharacterMissions.Single().progress == 1,
            "成功挑战记次数和结束时间并增加一次角色任务进度");
        operations.Execute(1, () =>
        {
            Check.That(!challenges.CompletePlay("session", new UserChallengeLiveClearRequest { life = 1000 }),
                "重复完成不再计次");
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userCharacterMissions.Single().progress == 1, "重复完成不增加角色任务");
        Reset();
        void UpdateMissions() => challenges.UpdateMissions(new UserChallengeLiveStartRequest { characterId = 1 },
            new UserChallengeLiveClearRequest { life = 1000 });
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            UpdateMissions();
            return new BrokenResponse();
        }), "挑战任务刷新编码失败");
        Check.That(store.Read(1)!.Data.userLiveMissions?.Any() != true &&
            store.Read(1)!.Data.userBeginnerMissionV2s?.Any() != true, "编码失败回滚挑战两类任务");
        operations.Execute(1, () => { UpdateMissions(); return user.BuildRefresh(); });
        Check.That(store.Read(1)!.Data.userLiveMissions.Single().progress == 30 &&
            store.Read(1)!.Data.userBeginnerMissionV2s.Single().beginnerMissionV2Id == 71 &&
            store.Read(1)!.Data.userMissionStatuses.Count(s => s.missionStatus == "achieved") == 2,
            "挑战增加配置点数并达成两类任务，不推进普通 Live 新手任务");
        operations.Execute(1, () => { UpdateMissions(); return user.BuildRefresh(); });
        Check.That(store.Read(1)!.Data.userLiveMissions.Single().progress == 60 &&
            store.Read(1)!.Data.userBeginnerMissionV2s.Single().progress == 1,
            "后续挑战累计 Live 点数，已达成新手任务不重复累计");
        Reset();
        operations.Execute(1, () =>
        {
            var result = challenges.AdvanceStage(new UserChallengeLiveStartRequest { characterId = 1 },
                new UserChallengeLiveClearRequest { score = 429, life = 1000 });
            Check.That(result.addPoint == 50 && result.afterRank == 2 && result.afterPoint == 0,
                "按结算分数计算点数并推进阶段");
            return user.BuildRefresh();
        });
        Check.Throws<NotSupportedException>(() => operations.Execute(1, () => challenges.AdvanceStage(
            new UserChallengeLiveStartRequest { characterId = 1, isAuto = true },
            new UserChallengeLiveClearRequest { score = 429, life = 1000 })), "自动挑战不套用普通点数规则");
        Check.Throws<NotSupportedException>(() => operations.Execute(1, () => challenges.AdvanceStage(
            new UserChallengeLiveStartRequest { characterId = 1 },
            new UserChallengeLiveClearRequest { score = 429, life = 0 })), "失败挑战不套用成功点数规则");
        Reset();
        Advance(49);
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloStages.Single().point == 49 &&
            store.Read(1)!.Data.userChargedCurrency == null, "挑战未达门槛不发阶段奖励");
        operations.Execute(1, () =>
        {
            var result = challenges.AdvanceStage(1, 1);
            Check.That(result.beforeRank == 1 && result.beforePoint == 49 && result.afterRank == 2 &&
                result.afterPoint == 0 && result.characterExpResult.afterTotalExp == 1,
                "挑战恰好达到门槛进入下一级零余点");
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloStages.First().point == 50 &&
            store.Read(1)!.Data.userChallengeLiveSoloStages.First().challengeLiveStageStatus == "complete" &&
            store.Read(1)!.Data.userChargedCurrency.free == 50, "保留已完成阶段并按用途选择奖励盒");
        Advance(10);
        Check.That(store.Read(1)!.Data.userChargedCurrency.free == 50 &&
            store.Read(1)!.Data.userChallengeLiveSoloStages.Last().point == 10, "后续点数不重复发放已完成阶段奖励");
        Check.That(store.Read(1)!.Data.userCharacters.Single().characterRank == 2 &&
            store.Read(1)!.Data.userMaterials.Single().quantity == 2, "角色升到二级发材料，无新增阶段时不重复发放");
        Reset();
        operations.Execute(1, () =>
        {
            var result = challenges.AdvanceStage(1, 201);
            Check.That(result.afterRank == 3 && result.afterPoint == 51 && result.characterExpResult.afterTotalExp == 3 &&
                result.challengeStageRewards.Length == 2, "跨阶段累加逐级角色经验，保留两份阶段奖励");
            Check.That(result.characterExpResult.afterLevel == 3 && result.characterExpResult.afterExp == 0 &&
                result.characterRankUpRewards.Length == 2 && result.characterRankUpRewards[0].resourceId == 18,
                "角色经验读独立等级门槛，跨级奖励按等级排序");
            var refresh = user.BuildRefresh();
            Check.That(refresh.userChallengeLiveSoloStages.Length == 3 && refresh.userChargedCurrency.free == 200 &&
                refresh.userCharacters.Single().characterRank == 3, "挑战阶段、角色和两类奖励一并刷新");
            return refresh;
        });
        Reset();
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            challenges.AdvanceStage(1, 201);
            return new BrokenResponse();
        }), "挑战阶段结果编码失败");
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloStages == null && store.Read(1)!.Data.userChargedCurrency == null,
            "编码失败回滚全部阶段与奖励");
        Check.That(store.Read(1)!.Data.userCharacters.Single().totalExp == 0 && store.Read(1)!.Data.userMaterials.Length == 0,
            "编码失败同时回滚角色经验与升级材料");
        Check.Throws<NotSupportedException>(() => Advance(350), "尚未核验的 EX 阶段拒绝结算");
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloStages == null && store.Read(1)!.Data.userChargedCurrency == null,
            "跨入不支持阶段不提交此前奖励");
        Check.Throws<ArgumentOutOfRangeException>(() => Advance(-1), "拒绝负挑战点数");
        var other = TestUsers.Create(1);
        other.Data.userCharacters = [new() { characterId = 1, characterRank = 1 },
            new() { characterId = 2, characterRank = 2, totalExp = 2, exp = 1 }];
        other.Data.userChallengeLiveSoloStages = [new() { characterId = 2, rank = 1, point = 9,
            challengeLiveStageId = 201, challengeLiveStageType = "normal", challengeLiveStageStatus = "in_progress" }];
        store.Save(1, other);
        Advance(201);
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloStages.First().characterId == 2 &&
            store.Read(1)!.Data.userChallengeLiveSoloStages.First().point == 9, "挑战结算保留其他角色阶段");
        Check.That(store.Read(1)!.Data.userCharacters.Single(c => c.characterId == 2).totalExp == 2,
            "角色升级保留其他角色经验");
        Reset();
        var previous = store.Read(1)!;
        previous.Data.userCharacters.Single().characterRank = 3;
        previous.Data.userCharacters.Single().totalExp = 3;
        store.Save(1, previous);
        Check.Throws<NotSupportedException>(() => Advance(201), "带解锁奖励的角色等级暂不推测处理");
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloStages == null &&
            store.Read(1)!.Data.userCharacters.Single().totalExp == 3 && store.Read(1)!.Data.userChargedCurrency == null,
            "角色升级拒绝时一并回滚阶段及先前发放的奖励");
        Reset();
        previous = store.Read(1)!;
        previous.Data.userCharacters.Single().characterRank = 2;
        previous.Data.userCharacters.Single().totalExp = 1;
        store.Save(1, previous);
        operations.Execute(1, () =>
        {
            var result = challenges.AdvanceStage(1, 50);
            Check.That(result.characterExpResult.beforeTotalExp == 1 && result.characterExpResult.afterTotalExp == 2 &&
                result.characterExpResult.afterLevel == 2 && result.characterExpResult.afterExp == 1 &&
                result.characterRankUpRewards.Length == 0, "角色未跨级时保留余经验，不重发等级奖励");
            return user.BuildRefresh();
        });
        Reset();
        previous = store.Read(1)!;
        previous.Data.userCharacters.Single().characterRank = 4;
        previous.Data.userCharacters.Single().totalExp = 9;
        previous.Data.userCharacters.Single().exp = 3;
        store.Save(1, previous);
        Check.Throws<NotSupportedException>(() => Advance(50), "角色满级经验规则未核验时停止结算");
        Check.That(store.Read(1)!.Data.userCharacters.Single().totalExp == 9 &&
            store.Read(1)!.Data.userChallengeLiveSoloStages == null, "满级边界拒绝不写入阶段或角色经验");
    }
}
