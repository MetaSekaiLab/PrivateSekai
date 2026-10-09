extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class ProfileHonorChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "honor-fixture");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "honors.json"), """[{"id":1,"groupId":1},{"id":21,"groupId":6},{"id":31,"groupId":6}]""");
        File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"), """
            [{"id":91,"beginnerMissionV2Type":"set_profile_honors_full","requirement":1},
             {"id":93,"beginnerMissionV2Type":"set_profile_honors_full","requirement":3},
             {"id":99,"beginnerMissionV2Type":"watch_any_music_video_full","requirement":1}]
            """);
        File.WriteAllText(Path.Combine(directory, "honorBackgrounds.json"), """[{"id":10101,"honorGroupId":1},{"id":10102,"honorGroupId":1},{"id":10601,"honorGroupId":6}]""");
        File.WriteAllText(Path.Combine(directory, "honorWords.json"), """[{"id":10101,"honorGroupId":1},{"id":10102,"honorGroupId":1},{"id":10601,"honorGroupId":6}]""");
        var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userHonors = [new() { honorId = 1, level = 1 }, new() { honorId = 21, level = 1 }, new() { honorId = 31, level = 1 }];
        state.Data.userBeginnerMissionV2s = [];
        state.Data.userMissionStatuses = [];
        state.Data.userHonorBackgrounds = [new() { honorBackgroundId = 10101 }, new() { honorBackgroundId = 10601 }];
        state.Data.userHonorWords = [new() { honorWordId = 10101 }, new() { honorWordId = 10601 }];
        state.Data.userProfileHonors = [];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton<IUserStore>(store)
            .AddSingleton(master).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var honors = new ProfileHonorService(user, master, scope.ServiceProvider.GetRequiredService<MissionService>());
        var request = new PutUserProfileHonorRequest { profileHonors = [Honor(3, 21), Honor(1, 1)] };
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            honors.Save(request);
            return new BrokenResponse();
        }), "称号编码失败回滚");
        Check.That(store.Read(1)!.Data.userProfileHonors.Length == 0, "称号编码失败保留原装备");
        var bytes = operation.Execute(1, () =>
        {
            Check.That(honors.Save(request) == 200, "保存持有称号成功");
            return user.BuildRefresh();
        });
        var refresh = DumpSerializer.Deserialize<SuiteUser>(bytes);
        Check.That(refresh.userProfileHonors.Select(h => h.seq).SequenceEqual(new[] { 1, 3 }), "称号按槽位排序");
        Check.That(request.profileHonors[0].seq == 3 && store.Read(2)!.Data.userProfileHonors == null,
            "称号保存不修改请求顺序或其他账号");
        request.profileHonors = [Honor(2, 21)];
        request.profileHonors[0].honorLevel = 0;
        operation.Execute(1, () => { honors.Save(request); return null; });
        Check.That(store.Read(1)!.Data.userProfileHonors.Single().seq == 2 &&
            store.Read(1)!.Data.userProfileHonors.Single().honorLevel == 1, "全量替换旧槽位并使用持有等级");
        request.profileHonors[0].honorLevel = 2;
        bytes = operation.Execute(1, () => { honors.Save(request); return user.BuildRefresh(); });
        Check.That(DumpSerializer.Deserialize<SuiteUser>(bytes).userProfileHonors.Single().honorLevel == 1,
            "过高等级归一化且同值保存仍返回装备");
        foreach (var invalid in new[] { Honor(1, 2), Honor(1, 1, 10601, 10101), Honor(1, 1, 10101, 10601),
            Honor(1, 1, 10102, 10101), Honor(1, 1, 10101, 10102) })
        {
            request.profileHonors = [Honor(3, 1), invalid];
            operation.Execute(1, () =>
            {
                Check.That(honors.Save(request) == 409 && user.BuildRefresh().userProfileHonors == null,
                    "拒绝未持有称号、未持有或跨组装饰，不产生刷新");
                return null;
            });
            Check.That(store.Read(1)!.Data.userProfileHonors.Single().seq == 2, "拒绝不部分更新装备");
        }
        request.profileHonors = [];
        bytes = operation.Execute(1, () => { honors.Save(request); return user.BuildRefresh(); });
        Check.That(DumpSerializer.Deserialize<SuiteUser>(bytes).userProfileHonors.Length == 0 &&
            store.Read(1)!.Data.userHonors.Length == 3, "清空返回空数组且保留持有称号");
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.Length == 0, "零至两槽不推进填满称号新手任务");
        request.profileHonors = [Honor(3, 31), Honor(1, 1), Honor(2, 21)];
        operation.Execute(1, () => { honors.Save(request); return user.BuildRefresh(); });
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.Select(m => m.beginnerMissionV2Id).SequenceEqual([91, 93]) &&
            store.Read(1)!.Data.userBeginnerMissionV2s.All(m => m.progress == 1) &&
            store.Read(1)!.Data.userMissionStatuses.Single().missionId == 91,
            "三槽全量保存按任务类型推进全部master门槛，不绑定任务编号或槽位顺序");
        state = store.Read(1)!;
        state.Data.userMissionStatuses.Single().missionStatus = "received";
        store.Save(1, state);
        operation.Execute(1, () => { honors.Save(request); return user.BuildRefresh(); });
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.All(m => m.progress == 2) &&
            store.Read(1)!.Data.userMissionStatuses.Single().missionStatus == "received", "同值三槽保存继续累计，保留已领取状态");
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            honors.Save(request);
            return new BrokenResponse();
        }), "三槽任务跨门槛编码失败");
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.All(m => m.progress == 2) &&
            store.Read(1)!.Data.userMissionStatuses.Length == 1, "编码失败回滚任务累计和新达成状态");
        operation.Execute(1, () => { honors.Save(request); return user.BuildRefresh(); });
        Check.That(store.Read(1)!.Data.userMissionStatuses.Single(m => m.missionId == 93).missionStatus == "achieved" &&
            store.Read(1)!.Data.userMissionStatuses.Single(m => m.missionId == 91).missionStatus == "received",
            "重复保存可跨其他master门槛，不覆盖已领取状态");
        request.profileHonors = [Honor(1, 1), Honor(2, 21), Honor(3, 999)];
        operation.Execute(1, () =>
        {
            Check.That(honors.Save(request) == 409, "三槽请求中未持有的称号仍被拒绝");
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.All(m => m.progress == 3) &&
            store.Read(1)!.Data.userProfileHonors.All(h => h.honorId != 999), "拒绝的三槽请求不推进任务或部分更换称号");
    }

    private static UserProfileHonor Honor(int seq, int id, int? background = null, int? word = null) => new()
    {
        seq = seq, honorId = id, honorLevel = 1, profileHonorType = "normal", bondsHonorViewType = "none",
        bondsHonorWordId = 0, honorBackgroundId = background, honorWordId = word
    };
}
