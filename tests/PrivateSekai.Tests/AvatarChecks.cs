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

internal static class AvatarChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "avatar-fixture");
        Directory.CreateDirectory(directory);
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userAvatar = new() { avatarSkinColorId = 5 };
        state.Data.userAvatarCostumes = [new() { avatarCostumeId = 1 }, new() { avatarCostumeId = 2 }];
        state.Data.userAvatarAccessories = [new() { avatarAccessoryId = 3 }, new() { avatarAccessoryId = 4 }];
        state.Data.userAvatarSkinColors = [new() { avatarSkinColorId = 5 }, new() { avatarSkinColorId = 6 }];
        state.Data.userAvatarCoordinates = [new() { avatarCoordinateId = 7 }];
        state.Data.userAvatarMotions = [new() { avatarMotionId = 8 }];
        state.Data.userAvatarMotionFavorites = [new() { avatarMotionId = 8, num = 0 }];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        var otherState = store.Read(1)!.Data;
        otherState.userAvatar = null;
        var otherBytes = DumpSerializer.Serialize(otherState);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        using var thumbnails = new CustomProfileThumbnailStore();
        var profiles = new ProfileService(user, thumbnails, new MasterData(new MasterCacheConfig { PinTables = [] }, directory));
        (int Status, UserAvatar? Avatar) Save(UserAvatarRequest request)
        {
            var status = 0;
            var bytes = operations.Execute(1, () =>
            {
                status = profiles.SaveAvatar(request);
                return status == 200 ? (object)new UserAvatarResponse { updatedResources = user.BuildRefresh() }
                    : new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = "", ErrorMessage = "" };
            });
            return (status, status == 200 ? DumpSerializer.Deserialize<UserAvatarResponse>(bytes).updatedResources.userAvatar : null);
        }
        var request = new UserAvatarRequest { avatarCostumeId = 1, avatarAccessoryId = 3, avatarSkinColorId = 5 };
        var first = Save(request);
        Check.That(first.Status == 200 && first.Avatar?.avatarCostumeId == 1 && first.Avatar.avatarAccessoryId == 3,
            "保存已持有服装和配件并返回完整外观");
        Check.That(Save(request).Avatar?.avatarAccessoryId == 3, "相同外观重复保存仍返回外观字段");
        var replaced = Save(new() { avatarCostumeId = 2, avatarAccessoryId = 4, avatarSkinColorId = 6 });
        Check.That(replaced.Avatar?.avatarCostumeId == 2 && replaced.Avatar.avatarAccessoryId == 4 && replaced.Avatar.avatarSkinColorId == 6,
            "服装、配件和肤色一次替换并持久化");
        var removed = Save(new() { avatarCostumeId = 2, avatarSkinColorId = 6 });
        Check.That(removed.Avatar?.avatarCostumeId == 2 && removed.Avatar.avatarAccessoryId == null,
            "未提交配件表示卸下，保留请求指定服装");
        var colorOnly = Save(new() { avatarSkinColorId = 5 });
        Check.That(colorOnly.Avatar?.avatarCostumeId == null && colorOnly.Avatar.avatarAccessoryId == null && colorOnly.Avatar.avatarSkinColorId == 5,
            "只提交肤色会清除其他穿戴，不按局部补丁合并");
        var coordinate = Save(new() { avatarCoordinateId = 7, avatarSkinColorId = 6 });
        Check.That(coordinate.Avatar?.avatarCoordinateId == 7 && coordinate.Avatar.avatarCostumeId == null && coordinate.Avatar.avatarAccessoryId == null,
            "客户端套装请求保存已持有套装和肤色");
        Check.That(Save(request).Avatar?.avatarCoordinateId == null, "切回服装和配件后清除套装");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        foreach (var invalid in new[]
        {
            new UserAvatarRequest { avatarCostumeId = 999, avatarSkinColorId = 5 },
            new UserAvatarRequest { avatarCostumeId = 1, avatarAccessoryId = 999, avatarSkinColorId = 5 },
            new UserAvatarRequest { avatarCostumeId = 1, avatarAccessoryId = 3, avatarSkinColorId = 999 },
            new UserAvatarRequest { avatarCoordinateId = 999, avatarSkinColorId = 5 }
        })
            Check.That(Save(invalid).Status == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "任一未持有外观整体拒绝，不部分更新其余穿戴");
        Check.That(Save(new()).Status == 500 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
            "缺失肤色保留原状态，状态码与官方空请求一致");
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            profiles.SaveAvatar(new() { avatarCostumeId = 2, avatarSkinColorId = 6 });
            return new BrokenResponse();
        }), "外观响应编码失败回滚");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败保留原外观与所有资源");
        operations.Execute(1, () =>
        {
            profiles.SaveAvatar(request);
            request.avatarAccessoryId = 4;
            Check.That(user.Data.userAvatar.avatarAccessoryId == 3, "保存的外观与请求对象引用隔离");
            return user.BuildRefresh();
        });
        var after = store.Read(1)!.Data;
        after.userAvatar = null;
        Check.That(DumpSerializer.Serialize(after).SequenceEqual(otherBytes) && DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer),
            "穿戴不扣资源、不改持有或快捷动作，也不影响其他账号");
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N"), "users.db");
        using (var sql = new SqliteUserStore(path)) sql.Save(1, store.Read(1)!);
        using (var sql = new SqliteUserStore(path))
            Check.That(sql.Read(1)!.Data.userAvatar.avatarCostumeId == 1 && sql.Read(1)!.Data.userAvatar.avatarAccessoryId == 3 &&
                sql.Read(1)!.Data.userAvatar.avatarCoordinateId == null, "SQLite重启保持穿戴及空套装字段");
    }
}
