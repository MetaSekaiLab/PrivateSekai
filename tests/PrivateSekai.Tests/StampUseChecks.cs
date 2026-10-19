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

internal static class StampUseChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "stamp-use-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "stamps.json"), """
            [{"id":10,"stampType":"illustration"},{"id":11,"stampType":"illustration"},
             {"id":20,"stampType":"illustration"},{"id":30,"stampType":"cheerful_carnival_message"}]
            """);
        File.WriteAllText(Path.Combine(directory, "honorMissions.json"), """
            [{"id":12,"honorMissionType":"multi_live_stamp","requirement":5,"rewards":[{"resourceBoxId":2}]},
             {"id":11,"honorMissionType":"multi_live_stamp","requirement":3,"rewards":[{"resourceBoxId":1}]},
             {"id":99,"honorMissionType":"unrelated","requirement":1}]
            """);
        File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
            [{"id":1,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"honor","resourceId":134,"resourceLevel":1,"resourceQuantity":1}]},
             {"id":2,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"coin","resourceQuantity":7}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"), "[]");
        File.WriteAllText(Path.Combine(directory, "characterMissionV2s.json"), "[]");
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userStamps = [new() { stampId = 10 }, new() { stampId = 11 }];
        state.Data.userHonors = [];
        state.Data.userProfile = new() { userId = 1, profileImageType = "leader" };
        state.Data.userProfileHonors = [];
        state.Data.userMissionStatuses = [];
        state.Data.userHonorMissions = [new() { honorMissionType = "aaa", progress = 2, achievedMissionIds = [] },
            new() { honorMissionType = "zzz", progress = 4, achievedMissionIds = [] }];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<IUserStore>(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var missions = scope.ServiceProvider.GetRequiredService<MissionService>();
        var master = scope.ServiceProvider.GetRequiredService<MissionMasterQueries>();
        UserStampUseHistory Use(int id, int count) => new() { stampId = id, useCount = count };
        (int Status, SuiteUser? Refresh) Report(UserStampUseHistoryRequest request)
        {
            var status = 0;
            var bytes = operations.Execute(1, () =>
            {
                var previous = HonorMissionResponse.AchievedIds(user.Data);
                status = missions.RecordStampUse(request);
                if (status != 200) return (object)new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = "", ErrorMessage = "" };
                var refresh = user.BuildRefresh();
                HonorMissionResponse.AddAchievementHints(refresh, previous, master);
                return new SuiteUserCommonResponse { updatedResources = refresh };
            });
            return (status, status == 200 ? DumpSerializer.Deserialize<SuiteUserCommonResponse>(bytes).updatedResources : null);
        }
        SuiteUser Advance(params UserStampUseHistory[] rows) => Report(new() { userStampUseHistories = rows.ToList() }).Refresh!;
        UserHonorMission Progress() => store.Read(1)!.Data.userHonorMissions.Single(m => m.honorMissionType == "multi_live_stamp");
        Check.That(Advance(Use(10, 1)).userHonorMissions.Select(m => m.honorMissionType).SequenceEqual(["aaa", "multi_live_stamp", "zzz"]) &&
            Progress().progress == 1, "首次表情上报创建按类型排序的称号进度，保留其他类型");
        Check.That(Advance(Use(10, 1)).userHonorMissions.Single(m => m.honorMissionType == "multi_live_stamp").progress == 2,
            "相同批次再次提交继续累计，不按表情编号去重历史请求");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        foreach (var entry in new[]
        {
            (Request: new UserStampUseHistoryRequest(), Status: 500),
            (Request: new UserStampUseHistoryRequest { userStampUseHistories = [null!] }, Status: 500),
            (Request: new UserStampUseHistoryRequest { userStampUseHistories = [Use(10, 1), Use(10, 1)] }, Status: 500),
            (Request: new UserStampUseHistoryRequest { userStampUseHistories = [Use(20, 1), Use(20, 1)] }, Status: 500),
            (Request: new UserStampUseHistoryRequest { userStampUseHistories = [Use(20, 0)] }, Status: 409),
            (Request: new UserStampUseHistoryRequest { userStampUseHistories = [Use(0, 0)] }, Status: 409),
            (Request: new UserStampUseHistoryRequest { userStampUseHistories = [Use(10, 2), Use(int.MaxValue, 1)] }, Status: 409)
        })
            Check.That(Report(entry.Request).Status == entry.Status && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "缺失、空项及重复表情先拒绝，未持有普通表情和未知表情整单拒绝且不推进任务");
        foreach (var rows in new[] { Array.Empty<UserStampUseHistory>(), new[] { Use(10, 0) }, new[] { Use(10, -1) }, new[] { Use(10, 1), Use(11, -1) } })
            Check.That(Advance(rows).userHonorMissions == null && Progress().progress == 2,
                "空批次与非正合计成功但不刷新或扣减称号进度");
        before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            Check.That(missions.RecordStampUse(new() { userStampUseHistories = [Use(10, 1)] }) == 200 &&
                user.Data.userMissionStatuses.Any(s => s.missionId == 11), "回滚检查实际跨越表情门槛并达成任务");
            return new BrokenResponse();
        }), "表情上报响应编码失败回滚");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "响应失败不保留次数或新达成的任务");
        var crossed = Advance(Use(30, 2), Use(11, -1));
        Check.That(crossed.userHonorMissions.Single(m => m.honorMissionType == "multi_live_stamp").achievedMissionIds.SequenceEqual([11]) &&
            Progress().progress == 3 && Progress().achievedMissionIds.Length == 0 && store.Read(1)!.Data.userHonors.Length == 0,
            "未持有Cheerful临时表情允许上报，批次先合计，跨门槛只在本次响应提示且不自动发奖");
        UserMissionReceiveResponse Receive(int id) => DumpSerializer.Deserialize<UserMissionReceiveResponse>(operations.Execute(1, () =>
        {
            var result = missions.ReceiveHonorMissionRewards([id]);
            Check.That(result.Status == 200, "表情称号达成后允许通用领奖");
            return new UserMissionReceiveResponse { ObtainedRewards = result.Rewards, UpdatedResources = user.BuildRefresh() };
        }));
        Check.That(Receive(11).ObtainedRewards.Single().resourceId == 134 && store.Read(1)!.Data.userHonors.Single().level == 1,
            "表情称号奖励通过资源服务保存并返回实际奖励");
        Check.That(Advance(Use(10, 2)).userHonorMissions.Single(m => m.honorMissionType == "multi_live_stamp").achievedMissionIds.SequenceEqual([12]) &&
            store.Read(1)!.Data.userMissionStatuses.Single(s => s.missionId == 11).missionStatus == "received",
            "领奖后继续批量推进下一门槛，不覆盖已领取状态");
        Check.That(Receive(12).ObtainedRewards.Single().quantity == 7 && store.Read(1)!.Data.userGamedata.coin == 107,
            "表情称号门槛和奖励来自master，不硬编码任务或奖励");
        operations.Execute(1, () =>
        {
            var repeated = missions.ReceiveHonorMissionRewards([11]);
            Check.That(repeated.Status == 409 && repeated.Rewards.Length == 0, "表情称号领奖拒绝重复发奖");
            return new SuiteUserCommonResponse();
        });
        Check.That(Advance(Use(10, 1)).userHonorMissions.Single(m => m.honorMissionType == "multi_live_stamp").achievedMissionIds.Length == 0 &&
            Progress().progress == 6 && store.Read(1)!.Data.userMissionStatuses.All(s => s.missionStatus == "received" && s.missionId != 99),
            "超过最高门槛继续累计，不重复提示或发奖，不混入其他任务类型");
        var path = Path.Combine(directory, "users.db");
        using (var sql = new SqliteUserStore(path)) sql.Save(1, store.Read(1)!);
        using (var sql = new SqliteUserStore(path))
            Check.That(sql.Read(1)!.Data.userHonorMissions.Single(m => m.honorMissionType == "multi_live_stamp").progress == 6 &&
                sql.Read(1)!.Data.userMissionStatuses.All(s => s.missionStatus == "received") && sql.Read(1)!.Data.userHonors.Single().honorId == 134,
                "数据库重启保留表情进度、领奖状态与称号");
        Check.That(store.Read(1)!.Data.userStamps.Select(s => s.stampId).SequenceEqual([10, 11]) &&
            DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "表情计数不授予表情或改变其他账号");
        state = store.Read(1)!;
        state.Data.userHonorMissions = [];
        state.Data.userMissionStatuses = [];
        store.Save(1, state);
        Check.That(Advance(Use(10, 2), Use(11, 4)).userHonorMissions.Single().achievedMissionIds.Order().SequenceEqual([11, 12]) &&
            store.Read(1)!.Data.userMissionStatuses.Length == 2,
            "一批上报可同时跨越全部满足的门槛，不依赖master行顺序");
    }
}
