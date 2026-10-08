extern alias game;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using game::Sekai;
using MessagePack;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using EmptyResponse = PrivateSekai.Models.EmptyResponse;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;
using PrivateSekai.Modules.Profiles;

namespace PrivateSekai.Tests;

internal static class SqliteStoreChecks
{
    public static void Run()
    {
        VerifyThumbnails();
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../obj/sqlite-fixtures", Guid.NewGuid().ToString("N"), "users.db"));
        using (var store = new SqliteUserStore(path))
        {
            Check.That(store.Read(1) == null, "SQLite 不隐式创建未知用户");
            var original = TestUsers.Create(1);
            original.Data.userCards = [new UserCard { cardId = 81, level = 1 }];
            original.Private.StoryBookmarks["unit_story"] = [];
            store.Save(1, original);
            original.Data.userGamedata.coin = 999;
            Check.That(store.Read(1)!.Data.userGamedata.coin == 100, "SQLite 保存后不泄漏可变引用");
            Check.That(store.ReserveId() == 2, "SQLite ID 分配接续已有用户");
        }
        using (var store = new SqliteUserStore(path))
        using (var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            sql.Open();
            Check.That(store.ReserveId() == 3 && store.GetUserIds().SequenceEqual(new long[] { 1 }),
                "SQLite 重启保留 ID 序列和用户索引");
            var restored = store.Read(1)!;
            Check.That(restored.Data.userCards.Single().cardId == 81 && restored.Private.StoryBookmarks.ContainsKey("unit_story"),
                "SQLite 重启恢复协议状态和私有字典");
            restored.Data.userCards[0].level = 9;
            Check.That(store.Read(1)!.Data.userCards[0].level == 1, "SQLite 读取返回隔离副本");
            var stale = store.Read(1)!;
            var state = store.Read(1)!;
            state.Data.userGamedata.coin = 101;
            store.Save(1, state);
            Check.That(Scalar(sql, "SELECT count(*) FROM user_blocks WHERE revision=2") == 1 &&
                Scalar(sql, "SELECT revision FROM user_blocks WHERE name='data/userGamedata'") == 2,
                "只改金币时仅写 userGamedata 块，其他块保持原版本");
            store.Save(1, state);
            Check.That(Scalar(sql, "SELECT revision FROM users WHERE user_id=1") == 2, "无变化保存不产生状态写入");
            Check.Throws<InvalidOperationException>(() => store.Save(1, stale), "SQLite 拒绝过期副本覆盖新状态");

            state = store.Read(1)!;
            state.Data.userCards = null!;
            state.Private.UserLiveSessions["fixture-live"] = new() { UserLiveId = "fixture-live", MusicId = 1 };
            store.Save(1, state);
            Check.That(Scalar(sql, "SELECT count(*) FROM user_blocks WHERE revision=3") == 2,
                "清空数组和私有字典修改仅写入各自分块");
            using (var another = new SqliteUserStore(path))
            {
                var other = another.Read(1)!;
                Check.That(other.Data.userCards == null && other.Private.UserLiveSessions.ContainsKey("fixture-live"),
                    "null 清空和进行中 Live 可以从数据库恢复");
                other.Data.userGamedata.coin = 102;
                another.Save(1, other);
                Check.That(store.Read(1)!.Data.userGamedata.coin == 102, "缓存依据数据库版本刷新，避免其他实例更新后读旧状态");
                Check.Throws<InvalidOperationException>(() => store.Save(1, state), "跨实例更新后拒绝旧版本写入");
            }

            using var provider = TestUsers.Provider(store);
            using var scope = provider.CreateScope();
            var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
            {
                user.Data.userGamedata.coin = 103;
                return new BrokenResponse();
            }), "SQLite 保存前响应编码失败");
            Check.That(store.Read(1)!.Data.userGamedata.coin == 102, "响应编码失败不提交 SQLite");
            Execute(sql, """
                CREATE TRIGGER fail_private BEFORE UPDATE ON user_blocks
                WHEN new.name='private/InheritId' BEGIN SELECT RAISE(ABORT, 'fixture'); END;
                """);
            Check.Throws<SqliteException>(() => operation.Execute(1, () =>
            {
                user.Data.userGamedata.coin = 104;
                user.Private.InheritId = "fixture-id";
                return new EmptyResponse();
            }), "分块保存中途数据库失败向外返回");
            Check.That(store.Read(1)!.Data.userGamedata.coin == 102 && store.Read(1)!.Private.InheritId == "" &&
                Scalar(sql, "SELECT revision FROM users WHERE user_id=1") == 4,
                "写入中途失败回滚此前分块、用户版本和内存缓存");
            Execute(sql, "DROP TRIGGER fail_private");
            operation.Execute(1, () => { user.Data.userGamedata.coin = 105; return new EmptyResponse(); });
            Check.That(store.Read(1)!.Data.userGamedata.coin == 105, "SQLite 失败后仍可正常提交");
            Execute(sql, "UPDATE user_blocks SET format_version=2 WHERE name='data/userGamedata'");
        }
        using (var store = new SqliteUserStore(path))
            Check.Throws<NotSupportedException>(() => store.Read(1), "不支持的块版本拒绝读取，不能恢复为新号");
        using (var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            sql.Open();
            Execute(sql, "UPDATE user_blocks SET format_version=1, payload='{\"unknownFixture\":1}' WHERE name='data/userGamedata'");
        }
        using (var store = new SqliteUserStore(path))
            Check.Throws<JsonException>(() => store.Read(1), "未知存档字段拒绝读取，防止旧代码静默丢字段");
        Console.WriteLine("SQLite：分块写入、重启恢复、缓存隔离、版本冲突及事务回滚检查通过。");
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void VerifyThumbnails()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../obj/sqlite-fixtures", Guid.NewGuid().ToString("N"), "images.db"));
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1];
        var encoded = Convert.ToBase64String(png);
        string savedPath;
        using (var store = new SqliteUserStore(path))
        using (var thumbnails = new CustomProfileThumbnailStore(path))
        {
            var state = TestUsers.Create(1);
            state.Data.userCustomProfiles = [];
            state.Data.userCustomProfileCards = [];
            state.Data.userCustomProfileResourceUsages = [];
            store.Save(1, state);
            using var provider = TestUsers.Provider(store);
            using var scope = provider.CreateScope();
            var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            var profiles = new ProfileService(user, thumbnails, null!);
            operation.Execute(1, () =>
            {
                profiles.SaveCustomProfileCard(1, 1, new() { thumbnail = "data:image/png;base64," + encoded });
                return new EmptyResponse();
            });
            savedPath = store.Read(1)!.Data.userCustomProfileCards.Single().thumbnailPath;
            Check.That(thumbnails.SaveThumbnail(encoded, savedPath) == savedPath, "相同缩略图复用原路径");

            using var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            sql.Open();
            Execute(sql, """
                CREATE TRIGGER fail_image BEFORE INSERT ON custom_profile_thumbnails
                BEGIN SELECT RAISE(ABORT, 'fixture'); END;
                """);
            Check.Throws<SqliteException>(() => operation.Execute(1, () =>
            {
                profiles.SaveCustomProfileCard(1, 1, new() { thumbnail = Convert.ToBase64String([0xFF, 0xD8, 0xFF]) });
                return new EmptyResponse();
            }), "图片写入失败不能继续提交用户引用");
            Check.That(store.Read(1)!.Data.userCustomProfileCards.Single().thumbnailPath == savedPath,
                "图片写入失败保留原名片引用");
            Execute(sql, "DROP TRIGGER fail_image");
            Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
            {
                profiles.SaveCustomProfileCard(1, 1, new() { thumbnail = Convert.ToBase64String([0xFF, 0xD8, 0xFF]) });
                return new BrokenResponse();
            }), "图片保存后响应编码失败");
            Check.That(store.Read(1)!.Data.userCustomProfileCards.Single().thumbnailPath == savedPath,
                "用户操作失败保留原名片引用，未引用图片不影响已有状态");
        }
        using (var store = new SqliteUserStore(path))
        using (var thumbnails = new CustomProfileThumbnailStore(path))
        {
            var restoredPath = store.Read(1)!.Data.userCustomProfileCards.Single().thumbnailPath;
            var parts = restoredPath.Split('/');
            Check.That(restoredPath == savedPath &&
                thumbnails.TryGetThumbnail(parts[0], parts[1], out var bytes, out var type) &&
                bytes.SequenceEqual(png) && type == "image/png", "重启后用户引用、图片字节和类型共同恢复");
            Check.That(thumbnails.SaveThumbnail(encoded, restoredPath) == restoredPath, "重启后仍复用相同图片路径");
            thumbnails.TryGetThumbnail(parts[0], parts[1], out var copy, out _);
            copy[0] = 0;
            Check.That(thumbnails.TryGetThumbnail(parts[0], parts[1], out copy, out _) && copy[0] == 0x89,
                "读取图片不泄漏可变引用");
            Check.That(!thumbnails.TryGetThumbnail("invalid", parts[1], out _, out _) &&
                !thumbnails.TryGetThumbnail(parts[0], Guid.NewGuid().ToString(), out _, out _), "非法或缺失图片返回未找到");
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
