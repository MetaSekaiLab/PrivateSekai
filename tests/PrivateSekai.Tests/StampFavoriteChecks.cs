extern alias game;

using System;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class StampFavoriteChecks
{
    public static void Run()
    {
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userStamps = [new() { stampId = 1 }, new() { stampId = 2 }];
        state.Data.UserStampFavoriteTabs = [];
        state.Data.userStampFavorites = [];
        store.Save(1, state);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var profiles = new ProfileService(user, new CustomProfileThumbnailStore(), null!);
        var request = new UserStampFavoriteRequest { UserStampFavoriteResource = new()
        {
            UserStampFavoriteTabs = [new() { TabNum = 0, TabName = "fixture" }],
            userStampFavorites = [new() { stampId = 1, TabNum = 0, num = 19 }, new() { stampId = 1, TabNum = 1, num = 0 }]
        } };
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            profiles.SaveStampFavorites(request);
            return new BrokenResponse();
        }), "表情收藏响应编码失败时回滚");
        Check.That(store.Read(1)!.Data.userStampFavorites.Length == 0 &&
            store.Read(1)!.Data.UserStampFavoriteTabs.Length == 0, "编码失败不留下页签或收藏");
        var bytes = operation.Execute(1, () =>
        {
            profiles.SaveStampFavorites(request);
            return user.BuildRefresh();
        });
        var refresh = DumpSerializer.Deserialize<SuiteUser>(bytes);
        Check.That(refresh.userStampFavorites.Length == 2 && refresh.UserStampFavoriteTabs.Single().userId == 1 &&
            refresh.userStampFavorites.All(f => f.userId == 1), "同表情跨页签和第 20 个槽位保存并刷新所属账号");
        request.UserStampFavoriteResource.userStampFavorites = [new() { stampId = 2, TabNum = 2, num = 3 }];
        operation.Execute(1, () => { profiles.SaveStampFavorites(request); return null; });
        Check.That(store.Read(1)!.Data.userStampFavorites.Single().stampId == 2, "收藏提交替换全量列表，删除未提交的位置");
        foreach (var invalid in new UserStampFavorite[][]
        {
            [new() { stampId = 3 }], [new() { stampId = 1, userId = 2 }],
            [new() { stampId = 1, num = 20 }], [new() { stampId = 1, TabNum = 3 }],
            [new() { stampId = 1 }, new() { stampId = 2 }],
            [new() { stampId = 1 }, new() { stampId = 1, num = 1 }]
        })
        {
            request.UserStampFavoriteResource.userStampFavorites = invalid;
            Check.Throws<ArgumentException>(() => operation.Execute(1, () =>
            {
                profiles.SaveStampFavorites(request);
                return null;
            }), "拒绝无效收藏、未持有表情和其他账号记录");
        }
        Check.That(store.Read(1)!.Data.userStampFavorites.Single().stampId == 2, "拒绝收藏配置不改变原状态");
        request.UserStampFavoriteResource.UserStampFavoriteTabs = [];
        request.UserStampFavoriteResource.userStampFavorites = [];
        operation.Execute(1, () => { profiles.SaveStampFavorites(request); return user.BuildRefresh(); });
        Check.That(store.Read(1)!.Data.userStampFavorites.Length == 0 &&
            store.Read(1)!.Data.UserStampFavoriteTabs.Length == 1 && store.Read(1)!.Data.userStamps.Length == 2,
            "清空收藏保留未提交的页签和持有表情");
        bytes = operation.Execute(1, () => { profiles.SaveStampFavorites(request); return user.BuildRefresh(); });
        refresh = DumpSerializer.Deserialize<SuiteUser>(bytes);
        Check.That(refresh.UserStampFavoriteTabs == null && refresh.userStampFavorites == null,
            "重复保存空收藏不返回未变化的收藏和页签");
    }
}
