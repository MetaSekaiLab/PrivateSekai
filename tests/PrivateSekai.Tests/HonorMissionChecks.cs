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

internal static class HonorMissionChecks
{
    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../obj/honor-mission-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "honorMissions.json"), """
                [{"id":1,"honorMissionType":"send_friend_request","requirement":1,"rewards":[{"resourceBoxId":1}]},
                 {"id":2,"honorMissionType":"make_friend","requirement":100,"rewards":[{"resourceBoxId":1}]}]
                """);
            File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
                [{"id":1,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"honor","resourceId":532,"resourceLevel":1,"resourceQuantity":1}]}]
                """);
            var store = new MemoryUserStore();
            using var provider = new ServiceCollection().AddPrivateSekai()
                .AddSingleton(_ => new CustomProfileThumbnailStore())
                .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
                .AddSingleton<IUserStore>(store).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            var missions = scope.ServiceProvider.GetRequiredService<MissionService>();
            var state = TestUsers.Create(1);
            state.Data.userProfile = new() { userId = 1, profileImageType = "leader" };
            state.Data.userHonors = [];
            state.Data.userProfileHonors = [];
            state.Data.userHonorMissions = [new() { honorMissionType = "send_friend_request", progress = 2, achievedMissionIds = [] }];
            state.Data.userMissionStatuses = [new() { userId = 1, missionType = "honor_mission", missionId = 1, missionStatus = "achieved" }];
            store.Save(1, state);
            foreach (var ids in new[] { new[] { 2 }, new[] { 1, 2 }, new[] { 1, 1 } })
            {
                var status = 0;
                operations.Execute(1, () =>
                {
                    status = missions.ReceiveHonorMissionRewards(ids).Status;
                    return user.BuildRefresh();
                });
                Check.That(status == 409 && store.Read(1)!.Data.userHonors.Length == 0 &&
                    store.Read(1)!.Data.userMissionStatuses.Single().missionStatus == "achieved",
                    "未达成、混合批量与重复 ID 均拒绝整单且不发奖");
            }
            Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
            {
                missions.ReceiveHonorMissionRewards([1]);
                return new BrokenResponse();
            }), "称号任务领取编码失败回滚奖励与任务状态");
            Check.That(store.Read(1)!.Data.userHonors.Length == 0 &&
                store.Read(1)!.Data.userMissionStatuses.Single().missionStatus == "achieved",
                "编码失败后仍可领取且没有残留称号");
            var received = DumpSerializer.Deserialize<UserMissionReceiveResponse>(operations.Execute(1, () =>
            {
                var result = missions.ReceiveHonorMissionRewards([1]);
                Check.That(result.Status == 200, "已达成称号任务允许领取");
                return new UserMissionReceiveResponse { ObtainedRewards = result.Rewards, UpdatedResources = user.BuildRefresh() };
            }));
            Check.That(received.ObtainedRewards.Single().resourceId == 532 &&
                store.Read(1)!.Data.userHonors.Single().level == 1 &&
                store.Read(1)!.Data.userMissionStatuses.Single().missionStatus == "received",
                "称号奖励持久化并标记任务已领取");
            Check.That(received.UpdatedResources.userHonorMissions == null &&
                store.Read(1)!.Data.userHonorMissions.Single().progress == 2 &&
                store.Read(1)!.Data.userHonorMissions.Single().achievedMissionIds.Length == 0,
                "领取不刷新或重写独立称号进度");
            operations.Execute(1, () =>
            {
                var repeated = missions.ReceiveHonorMissionRewards([1]);
                Check.That(repeated.Status == 409 && repeated.Rewards.Length == 0, "已领取称号任务拒绝重领");
                return user.BuildRefresh();
            });
            Check.That(store.Read(1)!.Data.userHonors.Length == 1, "重领失败不增加持有称号");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
