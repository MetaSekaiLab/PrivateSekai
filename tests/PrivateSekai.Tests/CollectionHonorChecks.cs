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

internal static class CollectionHonorChecks
{
    public static void Run()
    {
        foreach (var type in new[] { "collect_stamp", "collect_costume_3d", "collect_another_vocal" })
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "../../../obj/collection-honor", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "honorMissions.json"), $$"""
                    [{"id":13,"honorMissionType":"{{type}}","requirement":5,"rewards":[{"resourceBoxId":1}]},
                     {"id":11,"honorMissionType":"{{type}}","requirement":2,"rewards":[{"resourceBoxId":1}]},
                     {"id":12,"honorMissionType":"{{type}}","requirement":3,"rewards":[{"resourceBoxId":1}]},
                     {"id":99,"honorMissionType":"unrelated","requirement":1}]
                    """);
                File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"),
                    """[{"id":1,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"coin","resourceQuantity":5}]}]""");
                File.WriteAllText(Path.Combine(directory, "characterMissionV2s.json"), "[]");
                File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"), "[]");
                var store = new MemoryUserStore();
                using var provider = new ServiceCollection().AddPrivateSekai()
                    .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
                    .AddSingleton<IUserStore>(store).BuildServiceProvider();
                using var scope = provider.CreateScope();
                var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
                var user = scope.ServiceProvider.GetRequiredService<UserSession>();
                var missions = scope.ServiceProvider.GetRequiredService<MissionService>();
                var master = scope.ServiceProvider.GetRequiredService<MissionMasterQueries>();
                var state = TestUsers.Create(1);
                state.Data.userMissionStatuses = [];
                state.Data.userHonorMissions = [];
                store.Save(1, state);
                void Record()
                {
                    switch (type)
                    {
                        case "collect_stamp": missions.RecordStampPurchase(1); break;
                        case "collect_costume_3d": missions.RecordCostumeCraft(1); break;
                        default: missions.RecordAnotherVocalPurchase([1, 3]); break;
                    }
                }
                SuiteUser Advance() => DumpSerializer.Deserialize<SuiteUser>(operations.Execute(1, () =>
                {
                    var previous = HonorMissionResponse.AchievedIds(user.Data);
                    Record();
                    var refresh = user.BuildRefresh();
                    HonorMissionResponse.AddAchievementHints(refresh, previous, master);
                    return refresh;
                }));
                Check.That(Advance().userHonorMissions.Single().achievedMissionIds.Length == 0 &&
                    store.Read(1)!.Data.userMissionStatuses.Length == 0, "收集门槛前仅增加一次进度");
                Check.That(Advance().userHonorMissions.Single().achievedMissionIds.SequenceEqual([11]) &&
                    store.Read(1)!.Data.userHonorMissions.Single().achievedMissionIds.Length == 0 &&
                    store.Read(1)!.Data.userGamedata.coin == 100, "达到首门槛只产生状态与当次提示，不自动发奖或持久化提示");
                operations.Execute(1, () =>
                {
                    Check.That(missions.ReceiveHonorMissionRewards([11]).Status == 200, "收集称号达成后走通用领奖");
                    return user.BuildRefresh();
                });
                Check.That(Advance().userHonorMissions.Single().achievedMissionIds.SequenceEqual([12]) &&
                    store.Read(1)!.Data.userMissionStatuses.Single(s => s.missionId == 11).missionStatus == "received" &&
                    store.Read(1)!.Data.userGamedata.coin == 105, "领奖后继续收集跨第二门槛，不覆盖已领取状态或重发奖励");
                Check.That(Advance().userHonorMissions.Single().achievedMissionIds.Length == 0,
                    "两门槛之间不重复提示未领取的已达成任务");
                Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
                {
                    Record();
                    return new BrokenResponse();
                }), "跨收集门槛编码失败回滚");
                Check.That(store.Read(1)!.Data.userHonorMissions.Single().progress == 4 &&
                    store.Read(1)!.Data.userMissionStatuses.All(s => s.missionId != 13), "编码失败不保留新进度或任务状态");
                Check.That(Advance().userHonorMissions.Single().achievedMissionIds.SequenceEqual([13]) &&
                    Advance().userHonorMissions.Single().achievedMissionIds.Length == 0,
                    "高门槛按 master 达成，超过最大门槛仍可继续收集");
                state = store.Read(1)!;
                state.Data.userMissionStatuses = [];
                store.Save(1, state);
                Check.That(Advance().userHonorMissions.Single().achievedMissionIds.Order().SequenceEqual([11, 12, 13]) &&
                    store.Read(1)!.Data.userMissionStatuses.All(s => s.missionId != 99),
                    "已有高进度一次补齐所有满足的门槛，不依赖表行顺序且不混入其他类型");
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }
}
