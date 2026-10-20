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
using PrivateSekai.Modules.Missions;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class EventMissionChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "event-mission-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string value) => File.WriteAllText(Path.Combine(directory, name + ".json"), value);
        Table("events", """[{"id":1,"startAt":1000,"closedAt":3000},{"id":2,"startAt":1000,"closedAt":20000}]""");
        Table("liveMissionPeriods", """[{"id":1,"startAt":1000,"endAt":2500},{"id":2,"startAt":2500,"endAt":10000}]""");
        Table("liveMissions", """
            [{"id":1,"liveMissionPeriodId":1,"liveMissionType":"free","requirement":50},
             {"id":2,"liveMissionPeriodId":1,"liveMissionType":"free","requirement":100},
             {"id":3,"liveMissionPeriodId":1,"liveMissionType":"free","requirement":150},
             {"id":4,"liveMissionPeriodId":1,"liveMissionType":"premium","requirement":50},
             {"id":5,"liveMissionPeriodId":2,"liveMissionType":"free","requirement":50}]
            """);
        Table("resourceBoxes", """
            [{"id":10,"resourceBoxPurpose":"other","details":[{"resourceType":"jewel","resourceQuantity":999}]},
             {"id":10,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"live_point","resourceQuantity":50}]},
             {"id":11,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"jewel","resourceQuantity":10},{"resourceType":"live_point","resourceQuantity":50}]},
             {"id":12,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"gacha_ticket","resourceId":17,"resourceQuantity":2},{"resourceType":"live_point","resourceQuantity":10}]},
             {"id":13,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"material","resourceId":281,"resourceQuantity":7},{"resourceType":"live_point","resourceQuantity":50}]},
             {"id":14,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"future","resourceId":1,"resourceQuantity":1}]},
             {"id":15,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"live_point","resourceQuantity":0}]}]
            """);
        var definitions = new JsonArray();
        for (var id = 1; id <= 12; id++)
            definitions.Add(new JsonObject { ["id"] = id, ["eventId"] = id == 2 ? 2 : id == 9 ? 9 : 1,
                ["eventMissionCategory"] = id == 5 ? "complete_selectable_reward" : "normal",
                ["eventMissionSelectableRewardGroupId"] = id == 6 ? 1 : 0,
                ["rewards"] = id == 11 ? new JsonArray() : new JsonArray(new JsonObject {
                    ["missionId"] = id, ["missionType"] = id == 7 ? "honor_mission" : "event_mission",
                    ["resourceBoxId"] = id <= 4 ? id + 9 : id == 8 ? 14 : id == 10 ? 15 : id == 12 ? 99 : 10 }) });
        Table("eventMissions", definitions.ToJsonString());
        var clock = new Clock();
        var store = new MemoryUserStore();
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<TimeProvider>(clock)
            .AddSingleton<IUserStore>(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var session = scope.ServiceProvider.GetRequiredService<UserSession>();
        var service = scope.ServiceProvider.GetRequiredService<MissionService>();
        void Reset(int progress = 0)
        {
            clock.Now = 2000;
            var state = TestUsers.Create(1);
            state.Data.userLiveMissions = [new() { liveMissionPeriodId = 1, liveMissionStatus = "free", progress = progress, achievedMissionIds = [] }];
            state.Data.userChargedCurrency = new() { free = 100, paid = 3 };
            state.Data.userGachaTickets = [];
            state.Data.userMaterials = [];
            state.Data.userEventMissions = [new() { eventId = 1, eventMissionId = 1, progress = 10, isNewAchieved = false }];
            state.Data.userMissionStatuses = Enumerable.Range(1, 12).Select(id => new UserMissionStatus {
                userId = 1, missionType = "event_mission", missionId = id, missionStatus = "achieved" }).ToArray();
            store.Save(1, state);
        }
        (int Status, UserMissionReceiveResponse? Response) Receive(params int[] ids)
        {
            var status = 0;
            var bytes = operation.Execute(1, () => {
                var result = service.ReceiveEventMissionRewards(new() { missionIds = ids });
                status = result.Status;
                return status == 200 ? new UserMissionReceiveResponse { ObtainedRewards = result.Rewards,
                    UpdatedResources = session.BuildRefresh(prepareRefresh: false) } : null;
            });
            return (status, status == 200 ? DumpSerializer.Deserialize<UserMissionReceiveResponse>(bytes) : null);
        }
        Reset();
        var received = Receive(1);
        var data = store.Read(1)!.Data;
        Check.That(received.Status == 200 && received.Response!.ObtainedRewards.Single().resourceType == "live_point"
            && data.userLiveMissions.Single().progress == 50 && data.userChargedCurrency.free == 100,
            "普通活动任务从正确purpose发完整Live P奖励，忽略同号其他箱");
        Check.That(data.userMissionStatuses.Any(s => s.missionType == "event_mission" && s.missionId == 1 && s.missionStatus == "received")
            && data.userMissionStatuses.Any(s => s.missionType == "live_mission" && s.missionId == 1 && s.missionStatus == "achieved")
            && data.userLiveMissions.Single().achievedMissionIds.Length == 0 && data.userEventMissions.Single().progress == 10,
            "活动领奖推进当期免费Live任务，保留活动进度且不持久化临时提示");
        var snapshot = DumpSerializer.Serialize(data);
        Check.That(Receive(1).Status == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot), "活动任务拒绝重复领奖且不重复加点");
        received = Receive(2, 3, 4);
        data = store.Read(1)!.Data;
        Check.That(received.Status == 200 && received.Response!.ObtainedRewards.Select(r => r.resourceType).SequenceEqual(
            new[] { "jewel", "live_point", "gacha_ticket", "live_point", "material", "live_point" })
            && data.userLiveMissions.Single().progress == 160 && data.userChargedCurrency.free == 110 && data.userChargedCurrency.paid == 3
            && data.userGachaTickets.Single().quantity == 2 && data.userMaterials.Single().quantity == 7,
            "批量按请求和箱内顺序发宝石、抽卡券、材料与Live P，不吞附加项");
        Check.That(data.userMissionStatuses.Count(s => s.missionType == "live_mission") == 3
            && data.userMissionStatuses.All(s => s.missionType != "live_mission" || s.missionId != 4), "一次跨多档免费Live门槛，付费档不被免费资源误达成");
        foreach (var ids in new[] { new[] { 1, 1 }, new[] { 1, 2 } })
        {
            Reset();
            var state = store.Read(1)!;
            state.Data.userMissionStatuses.Single(s => s.missionId == 2).missionStatus = "received";
            store.Save(1, state);
            snapshot = DumpSerializer.Serialize(state.Data);
            Check.That(Receive(ids).Status == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot), "重复编号或混合已领取批量拒绝整单");
        }
        Reset();
        var missing = store.Read(1)!;
        missing.Data.userMissionStatuses = [];
        store.Save(1, missing);
        Check.That(Receive(1).Status == 409 && store.Read(1)!.Data.userLiveMissions.Single().progress == 0, "未达成或缺少状态不凭进度发奖");
        foreach (var (at, expected) in new[] { (999L, 409), (1000L, 200), (2999L, 200), (3000L, 409) })
        {
            Reset();
            clock.Now = at;
            snapshot = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.That(Receive(1).Status == expected, "活动任务领奖期起点包含、closedAt不包含");
            if (expected != 200) Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot), "期间拒绝保留全部状态");
        }
        Reset(75);
        clock.Now = 2500;
        Receive(1);
        data = store.Read(1)!.Data;
        Check.That(data.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress == 75
            && data.userLiveMissions.Single(m => m.liveMissionPeriodId == 2).progress == 50
            && data.userMissionStatuses.Any(s => s.missionType == "live_mission" && s.missionId == 5), "跨月领奖发到领取时当前Live周期，保留旧期进度");
        foreach (var id in new[] { 5, 6, 8, 10 })
        {
            Reset();
            snapshot = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.That(Receive(1, id).Status == 501 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot), "未知选择、资源或零奖品在批量发奖前拒绝");
        }
        foreach (var id in new[] { 7, 9, 11, 12 })
        {
            Reset();
            snapshot = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<InvalidOperationException>(() => Receive(1, id), "错误奖励关联、缺事件或缺奖励箱拒绝");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot), "坏配置不残留先前批量奖品");
        }
        Reset();
        foreach (var ids in new[] { Array.Empty<int>(), new[] { 0 }, new[] { 999 } })
            Check.That(Receive(ids).Status == 400, "无效活动任务编号拒绝");
        Reset();
        var rejected = 0;
        operation.Execute(1, () => { rejected = service.ReceiveEventMissionRewards(new() { missionIds = [1], EventMissionSelectableRewardId = 1 }).Status; return null; });
        Check.That(rejected == 501 && store.Read(1)!.Data.userLiveMissions.Single().progress == 0, "普通奖励入口不忽略自选奖励参数");
        Reset();
        var paid = store.Read(1)!;
        paid.Data.userLiveMissions.Single().liveMissionStatus = "premium";
        store.Save(1, paid);
        snapshot = DumpSerializer.Serialize(paid.Data);
        Check.That(Receive(2).Status == 501 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot),
            "付费轨道未核验时拒绝整笔活动点数奖励，不先发宝石或新增第二条免费进度");
        Check.Throws<NotSupportedException>(() => operation.Execute(1, () => {
            scope.ServiceProvider.GetRequiredService<ResourceService>().Grant(new UserResource { resourceType = "live_point", quantity = 50 });
            return null;
        }), "资源入口也不按免费规则处理付费轨道");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot), "付费轨道拒绝保持周期唯一记录和原任务状态");
        Reset(int.MaxValue - 25);
        snapshot = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<OverflowException>(() => Receive(2), "Live P溢出发生在宝石发放之后");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot), "Live P溢出回滚前项宝石、点数和任务状态");
        Reset();
        clock.Now = 11000;
        snapshot = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<InvalidOperationException>(() => Receive(2), "当前Live周期缺失不吞掉点数奖励");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot), "缺周期回滚前项奖励");
        Reset();
        snapshot = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () => {
            service.ReceiveEventMissionRewards(new() { missionIds = [1, 2] }); return new GachaExtraChecks.BrokenResponse(); }), "活动领奖响应编码失败");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(snapshot), "编码失败回滚整批奖励和两类任务");
        var outcomes = Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => {
            using var race = provider.CreateScope();
            var status = 0;
            race.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () => {
                status = race.ServiceProvider.GetRequiredService<MissionService>().ReceiveEventMissionRewards(new() { missionIds = [1] }).Status;
                return null;
            });
            return status;
        }))).GetAwaiter().GetResult();
        Check.That(outcomes.Order().SequenceEqual(new[] { 200, 409 }) && store.Read(1)!.Data.userLiveMissions.Single().progress == 50, "并发领奖只一笔成功并只加一次Live P");
        var path = Path.Combine(directory, "event-mission.db");
        using (var sqlite = new SqliteUserStore(path)) sqlite.Save(1, store.Read(1)!);
        using var restored = new SqliteUserStore(path);
        using var resumed = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<IUserStore>(restored).AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        using var restoredScope = resumed.CreateScope();
        restoredScope.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () => {
            Check.That(restoredScope.ServiceProvider.GetRequiredService<MissionService>().ReceiveEventMissionRewards(new() { missionIds = [1] }).Status == 409,
                "SQLite重启后仍拒绝已领奖任务");
            return restoredScope.ServiceProvider.GetRequiredService<MissionService>().ReceiveEventMissionRewards(new() { missionIds = [2] }).Status;
        });
        Check.That(restored.Read(1)!.Data.userLiveMissions.Single().progress == 100 && restored.Read(1)!.Data.userChargedCurrency.free == 110,
            "SQLite重启后继续领取另一任务，累计奖励和达成状态");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "活动领奖不影响其他账号");
    }

    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
}
