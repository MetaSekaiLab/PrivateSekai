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

internal static class PenlightChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "penlight-fixture");
        Directory.CreateDirectory(directory);
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userPenlights = [new() { penlightId = 11 }, new() { penlightId = 1, favoriteFlg = true }, new() { penlightId = 7 }];
        state.Data.userAvatar = new() { avatarSkinColorId = 1 };
        state.Data.userVirtualShops = [new() { virtualShopId = 9, userVirtualShopItems =
            [new() { virtualShopId = 9, virtualShopItemId = 99, status = "sale" }] }];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        var other = store.Read(1)!.Data;
        other.userPenlights = null;
        var otherBytes = DumpSerializer.Serialize(other);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        using var thumbnails = new CustomProfileThumbnailStore();
        var profiles = new ProfileService(user, thumbnails, new MasterData(new MasterCacheConfig { PinTables = [] }, directory));
        (int Status, UserPenlight[]? Refresh, UserVirtualShop[]? Shops) Save(int id, bool flag)
        {
            var status = 0;
            var bytes = operations.Execute(1, () =>
            {
                status = profiles.SavePenlight(id, flag);
                return status == 200 ? (object)new UserPenlightResponse { updatedResources = user.BuildRefresh() }
                    : new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = "", ErrorMessage = "" };
            });
            var refresh = status == 200 ? DumpSerializer.Deserialize<UserPenlightResponse>(bytes).updatedResources : null;
            return (status, refresh?.userPenlights, refresh?.userVirtualShops);
        }
        var selected = Save(11, true);
        Check.That(selected.Status == 200 && selected.Refresh!.Select(p => p.penlightId).SequenceEqual(new[] { 11, 1, 7 }) &&
            selected.Refresh.Single(p => p.favoriteFlg).penlightId == 11, "选择已持有荧光棒并取消旧选择，刷新全列表且保留顺序");
        Check.That(selected.Shops?.Single().userVirtualShopItems.Single().status == "sale", "选择变化时同时刷新完整商店但不改变商品状态");
        var repeat = Save(11, true);
        Check.That(repeat.Refresh == null && repeat.Shops == null, "重复选择不刷新荧光棒或商店列表");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        foreach (var id in new[] { 1, 11 })
            Check.That(Save(id, false).Refresh == null && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "false请求对已选和未选荧光棒都不取消、不刷新");
        foreach (var flag in new[] { true, false })
            Check.That(Save(999, flag).Status == 404 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "未持有荧光棒的true和false请求均404且不变更");
        Check.That(Save(7, true).Refresh!.Single(p => p.favoriteFlg).penlightId == 7, "第三支荧光棒切换后仍只有一项选中");
        before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            profiles.SavePenlight(11, true);
            return new BrokenResponse();
        }), "荧光棒响应编码失败回滚选择");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败保持第三支荧光棒选中");
        var after = store.Read(1)!.Data;
        after.userPenlights = null;
        Check.That(DumpSerializer.Serialize(after).SequenceEqual(otherBytes) && DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer),
            "荧光棒选择不扣资源、不改外观、不修改其他账号");
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N"), "users.db");
        using (var sql = new SqliteUserStore(path)) sql.Save(1, store.Read(1)!);
        using (var sql = new SqliteUserStore(path))
            Check.That(sql.Read(1)!.Data.userPenlights.Single(p => p.favoriteFlg).penlightId == 7, "SQLite重启保留荧光棒选择");
    }
}
