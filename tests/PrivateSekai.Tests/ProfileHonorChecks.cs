extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
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
        File.WriteAllText(Path.Combine(directory, "honors.json"), """[{"id":1,"groupId":1},{"id":21,"groupId":6}]""");
        File.WriteAllText(Path.Combine(directory, "honorBackgrounds.json"), """[{"id":10101,"honorGroupId":1},{"id":10102,"honorGroupId":1},{"id":10601,"honorGroupId":6}]""");
        File.WriteAllText(Path.Combine(directory, "honorWords.json"), """[{"id":10101,"honorGroupId":1},{"id":10102,"honorGroupId":1},{"id":10601,"honorGroupId":6}]""");
        var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userHonors = [new() { honorId = 1, level = 1 }, new() { honorId = 21, level = 1 }];
        state.Data.userHonorBackgrounds = [new() { honorBackgroundId = 10101 }, new() { honorBackgroundId = 10601 }];
        state.Data.userHonorWords = [new() { honorWordId = 10101 }, new() { honorWordId = 10601 }];
        state.Data.userProfileHonors = [];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var honors = new ProfileHonorService(user, master);
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
            store.Read(1)!.Data.userHonors.Length == 2, "清空返回空数组且保留持有称号");
    }

    private static UserProfileHonor Honor(int seq, int id, int? background = null, int? word = null) => new()
    {
        seq = seq, honorId = id, honorLevel = 1, profileHonorType = "normal", bondsHonorViewType = "none",
        bondsHonorWordId = 0, honorBackgroundId = background, honorWordId = word
    };
}
