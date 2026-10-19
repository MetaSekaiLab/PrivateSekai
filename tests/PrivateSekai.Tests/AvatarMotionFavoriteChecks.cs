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

internal static class AvatarMotionFavoriteChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "avatar-motion-favorite-fixture");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "configs.json"), """[{"configKey":"favorite_avatar_motion_max_count","value":"5"}]""");
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userAvatarMotions = Enumerable.Range(10, 6).Select(id => new UserAvatarMotion { avatarMotionId = id }).ToArray();
        state.Data.userAvatarMotionFavorites = [new() { avatarMotionId = 10, num = 0 }];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        using var thumbnails = new CustomProfileThumbnailStore();
        var profiles = new ProfileService(user, thumbnails, new MasterData(new MasterCacheConfig { PinTables = [] }, directory));
        (int Status, SuiteUser Refresh) Save(params (int Id, int Num)[] rows)
        {
            var status = 0;
            var bytes = operations.Execute(1, () =>
            {
                status = profiles.SaveAvatarMotionFavorites(new() { userAvatarMotionFavorites = rows
                    .Select(row => new UserAvatarMotionFavorite { avatarMotionId = row.Id, num = row.Num }).ToArray() });
                return user.BuildRefresh();
            });
            return (status, DumpSerializer.Deserialize<SuiteUser>(bytes));
        }
        var first = Save((12, 0), (10, 1));
        Check.That(first.Status == 200 && first.Refresh.userAvatarMotionFavorites.Select(f => (f.avatarMotionId, f.num))
            .SequenceEqual(new[] { (10, 1), (12, 0) }), "动作快捷栏全量替换，按动作编号返回并保留指定槽位");
        Check.That(Save((10, 1), (12, 0)).Refresh.userAvatarMotionFavorites.Length == 2,
            "相同非空快捷栏保存仍返回完整动作列表");
        Save((11, 4));
        Check.That(store.Read(1)!.Data.userAvatarMotionFavorites.Single().num == 4,
            "支持只保留末槽，不强行填充或重排空槽");
        Check.That(Save().Refresh.userAvatarMotionFavorites.Length == 0 && Save().Refresh.userAvatarMotionFavorites == null,
            "空数组清空快捷栏，重复清空省略未变化字段");
        Check.That(Save((14, 4), (13, 3), (12, 2), (11, 1), (10, 0)).Refresh.userAvatarMotionFavorites.Length == 5,
            "支持master定义的完整五槽快捷栏");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        foreach (var rows in new[] { new[] { (10, 0), (10, 1) }, new[] { (10, 0), (11, 0) } })
            Check.That(Save(rows).Status == 400 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "重复动作或重复槽位返回400并整体保留原状态");
        Check.That(Save(Enumerable.Range(10, 6).Select((id, num) => (id, num)).ToArray()).Status == 409 &&
            DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "六项列表包含越界槽位时返回409并保留原状态");
        foreach (var row in new[] { (999, 0), (0, 0), (10, -1), (10, 5) })
            Check.That(Save(row).Status == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "未持有动作或越界槽位返回409且不部分更新");
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            profiles.SaveAvatarMotionFavorites(new() { userAvatarMotionFavorites = [new() { avatarMotionId = 10, num = 1 }] });
            return new BrokenResponse();
        }), "动作快捷栏编码失败回滚");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败保持原五槽配置及其他资源");
        var input = new UserAvatarMotionFavorite { avatarMotionId = 11, num = 0 };
        operations.Execute(1, () =>
        {
            profiles.SaveAvatarMotionFavorites(new() { userAvatarMotionFavorites = [input] });
            input.num = 4;
            Check.That(user.Data.userAvatarMotionFavorites.Single().num == 0, "动作快捷栏保存与请求对象引用隔离");
            return user.BuildRefresh();
        });
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer) && store.Read(1)!.Data.userAvatarMotions.Length == 6,
            "修改快捷栏不发放动作，也不改变其他账号");
        var missing = DumpSerializer.Deserialize<SuiteUser>(operations.Execute(1, () =>
        {
            Check.That(profiles.SaveAvatarMotionFavorites(new()) == 200, "缺失动作列表按空列表保存");
            return user.BuildRefresh();
        }));
        Check.That(missing.userAvatarMotionFavorites.Length == 0 && store.Read(1)!.Data.userAvatarMotionFavorites.Length == 0,
            "缺失动作列表清空原有快捷栏并返回空列表刷新");
    }
}
