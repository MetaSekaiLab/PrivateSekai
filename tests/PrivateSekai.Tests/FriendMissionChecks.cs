extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class FriendMissionChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "friend-mission-fixture");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"), """
            [{"id":93,"beginnerMissionV2Type":"make_new_friend","requirement":3},
             {"id":91,"beginnerMissionV2Type":"make_new_friend","requirement":1},
             {"id":99,"beginnerMissionV2Type":"challenge_live_clear","requirement":1}]
            """);
        var store = new MemoryUserStore();
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton<IUserStore>(store)
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory)).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var state = TestUsers.Create(1);
        state.Data.userBeginnerMissionV2s = [];
        state.Data.userMissionStatuses = [];
        state.Data.userFriends = [new() { opponentUserId = 2, friendStatus = "pending_request" },
            new() { opponentUserId = 3, friendStatus = "sent_request" }];
        store.Save(1, state);
        void Refresh() => operations.Execute(1, () =>
        {
            var response = user.BuildRefresh();
            user.BuildSuite();
            user.BuildRefresh();
            return response;
        });
        Refresh();
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.Length == 0,
            "没有已确认好友时不建立好友任务，申请不计入好友数");
        state = store.Read(1)!;
        state.Data.userFriends[0].friendStatus = "friend";
        store.Save(1, state);
        Refresh();
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.All(m => m.progress == 1) &&
            store.Read(1)!.Data.userBeginnerMissionV2s.Select(m => m.beginnerMissionV2Id).SequenceEqual([91, 93]) &&
            store.Read(1)!.Data.userMissionStatuses.Single().missionId == 91,
            "单好友按master类型推进全部任务，同一操作多次构造刷新只累加一次");
        state = store.Read(1)!;
        state.Data.userFriends[1].friendStatus = "friend";
        state.Data.userMissionStatuses.Single().missionStatus = "received";
        store.Save(1, state);
        Refresh();
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.All(m => m.progress == 3 && !m.isNewAchieved) &&
            store.Read(1)!.Data.userMissionStatuses.Single(m => m.missionId == 91).missionStatus == "received" &&
            store.Read(1)!.Data.userMissionStatuses.Single(m => m.missionId == 93).missionStatus == "achieved",
            "双好友累加2并跨更高门槛，不覆盖已领取状态");
        Refresh();
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.All(m => m.progress == 5) &&
            store.Read(1)!.Data.userMissionStatuses.Length == 2, "重复刷新继续计数，不重复建立达成状态");
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            user.BuildRefresh();
            return new BrokenResponse();
        }), "好友任务刷新编码失败");
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.All(m => m.progress == 5), "编码失败回滚好友任务累积");
        operations.Execute(1, () => null);
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.All(m => m.progress == 5), "不构造资源刷新的操作不推进任务");
        operations.Query(1, () => user.BuildRefresh());
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.All(m => m.progress == 5), "只读查询不提交刷新副作用");
        store.Save(2, TestUsers.Create(2));
        operations.ExecutePair(1, 2, peer =>
        {
            user.Data.userFriends[1].friendStatus = "sent_request";
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.All(m => m.progress == 6) &&
            store.Read(2)!.Data.userBeginnerMissionV2s?.Any() != true,
            "双账号操作按业务后的好友数刷新调用方，不刷新对方任务");
    }
}
