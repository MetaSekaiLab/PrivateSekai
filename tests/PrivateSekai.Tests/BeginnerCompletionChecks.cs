extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class BeginnerCompletionChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "beginner-completion-fixture");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"), """
            [{"id":101,"beginnerMissionV2Category":"normal","requirement":1},
             {"id":102,"beginnerMissionV2Category":"normal","requirement":1},
             {"id":103,"beginnerMissionV2Category":"normal","requirement":1},
             {"id":201,"beginnerMissionV2Type":"achieve_all_missions","beginnerMissionV2Category":"complete","requirement":2,"rewards":[{"resourceBoxId":51}]},
             {"id":202,"beginnerMissionV2Type":"achieve_all_missions","beginnerMissionV2Category":"complete","requirement":3,"rewards":[{"resourceBoxId":52}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
            [{"id":51,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"jewel","resourceQuantity":17}]},
             {"id":52,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"coin","resourceQuantity":700}]}]
            """);
        var store = new MemoryUserStore();
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton<IUserStore>(store)
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory)).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var missions = scope.ServiceProvider.GetRequiredService<MissionService>();
        var state = TestUsers.Create(1);
        state.Data.userChargedCurrency = new();
        state.Data.userBeginnerMissionV2s = [];
        state.Data.userMissionStatuses = new[] { 101, 102, 103 }.Select(id => new UserMissionStatus
        {
            userId = 1, missionType = "beginner_mission_v2", missionId = id, missionStatus = "achieved"
        }).ToArray();
        store.Save(1, state);
        var initialJewel = state.Data.userChargedCurrency.free;
        var initialCoin = state.Data.userGamedata.coin;
        UserMissionReceiveResponse Receive(params int[] ids) => DumpSerializer.Deserialize<UserMissionReceiveResponse>(operations.Execute(1, () =>
        {
            var previous = BeginnerMissionResponse.AchievedIds(user.Data);
            var response = missions.ReceiveBeginnerMissionV2Rewards(ids);
            response.UpdatedResources = user.BuildRefresh();
            BeginnerMissionResponse.AddAchievementHints(response.UpdatedResources, previous);
            return response;
        }));
        Check.Throws<ArgumentException>(() => Receive(201), "未达成的总任务不能直接领奖");
        Receive(101);
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.Where(m => m.beginnerMissionV2Id >= 201).All(m => m.progress == 1) &&
            store.Read(1)!.Data.userMissionStatuses.All(m => m.missionId < 201), "按全部master总任务累计，门槛前不达成");
        Check.Throws<ArgumentException>(() => Receive(102, 999), "混合无效领奖整体回滚");
        Check.That(store.Read(1)!.Data.userMissionStatuses.Single(m => m.missionId == 102).missionStatus == "achieved" &&
            store.Read(1)!.Data.userBeginnerMissionV2s.Single(m => m.beginnerMissionV2Id == 201).progress == 1,
            "无效批次不消耗普通任务或推进总任务");
        var response = Receive(102, 102);
        Check.That(response.UpdatedResources.userBeginnerMissionV2s.Single(m => m.beginnerMissionV2Id == 201).isNewAchieved &&
            !response.UpdatedResources.userBeginnerMissionV2s.Single(m => m.beginnerMissionV2Id == 202).isNewAchieved &&
            store.Read(1)!.Data.userBeginnerMissionV2s.Where(m => m.beginnerMissionV2Id >= 201).All(m => m.progress == 2 && !m.isNewAchieved),
            "跨master门槛达成且重复ID只计一次，提示只在响应中");
        Check.That(store.Read(1)!.Data.userChargedCurrency.free == initialJewel, "达成总任务不会自动发放完成奖励");
        response = Receive(201);
        Check.That(response.ObtainedRewards.Single().resourceType == "jewel" && store.Read(1)!.Data.userChargedCurrency.free == initialJewel + 17 &&
            store.Read(1)!.Data.userBeginnerMissionV2s.Single(m => m.beginnerMissionV2Id == 202).progress == 2,
            "总任务按master独立发奖，完成类别不反向累计");
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            missions.ReceiveBeginnerMissionV2Rewards([103]);
            return new BrokenResponse();
        }), "最后一次普通任务领奖编码失败");
        Check.That(store.Read(1)!.Data.userMissionStatuses.Single(m => m.missionId == 103).missionStatus == "achieved" &&
            store.Read(1)!.Data.userMissionStatuses.All(m => m.missionId != 202), "编码失败回滚普通领奖和新总任务达成");
        response = Receive(103);
        Check.That(response.UpdatedResources.userBeginnerMissionV2s.Single(m => m.beginnerMissionV2Id == 202).isNewAchieved &&
            store.Read(1)!.Data.userMissionStatuses.Single(m => m.missionId == 201).missionStatus == "received",
            "继续跨更高门槛且保留已领取的总任务状态");
        Receive(202);
        Check.That(store.Read(1)!.Data.userGamedata.coin == initialCoin + 700 &&
            store.Read(1)!.Data.userBeginnerMissionV2s.Single(m => m.beginnerMissionV2Id == 202).progress == 3,
            "其他资源类型同样按master发奖，总任务领取不自增");
        Check.Throws<MissionAlreadyReceivedException>(() => Receive(202), "总任务完成奖励不能重复领取");
        Check.That(store.Read(1)!.Data.userGamedata.coin == initialCoin + 700, "重复领取不再次发奖");
    }
}
