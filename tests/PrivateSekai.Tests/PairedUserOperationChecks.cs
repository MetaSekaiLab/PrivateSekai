extern alias game;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class PairedUserOperationChecks
{
    public static void Run()
    {
        VerifyOperations(new MemoryUserStore());
        VerifyConcurrentPairs();
        VerifyMemoryBatchValidation();
        var path = Path.Combine(AppContext.BaseDirectory, "paired-store-fixtures", Guid.NewGuid().ToString("N"), "users.db");
        using (var store = new SqliteUserStore(path))
        {
            VerifyOperations(store);
            VerifyDatabaseRollback(store, path);
        }
        using (var restored = new SqliteUserStore(path))
            Check.That(restored.Read(1)!.Data.userGamedata.coin == 111 && restored.Read(2)!.Data.userGamedata.coin == 121,
                "双账号最终提交在 SQLite 重启后共同恢复");
        Console.WriteLine("双账号事务：业务及编码回滚、固定锁序、并发、SQLite 中途失败和版本冲突检查通过。");
    }

    private static void VerifyOperations(IUserStore store)
    {
        store.Save(1, TestUsers.Create(1));
        store.Save(2, TestUsers.Create(2));
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        Check.Throws<InvalidOperationException>(() => operation.ExecutePair(1, 2, peer =>
        {
            user.Data.userGamedata.coin = 1;
            peer.Data.userGamedata.coin = 2;
            throw new InvalidOperationException("fixture");
        }), "双账号业务异常向外返回");
        Check.That(store.Read(1)!.Data.userGamedata.coin == 100 && store.Read(2)!.Data.userGamedata.coin == 100,
            "业务异常不改变任一账号");
        Check.Throws<MessagePackSerializationException>(() => operation.ExecutePair(2, 1, peer =>
        {
            user.Data.userGamedata.coin = 3;
            peer.Private.InheritId = "fixture";
            return new BrokenResponse();
        }), "双账号编码失败向外返回");
        Check.That(store.Read(2)!.Data.userGamedata.coin == 100 && store.Read(1)!.Private.InheritId == "",
            "编码失败回滚双方协议及私有数据");
        Check.Throws<InvalidOperationException>(() => _ = user.Data, "双账号异常后关闭会话");
        var called = false;
        Check.Throws<KeyNotFoundException>(() => operation.ExecutePair(1, 999, _ => { called = true; return null; }),
            "对方不存在时拒绝，不创建账号");
        Check.That(!called && store.Read(999) == null, "缺失对方时不调用业务");
        Check.Throws<ArgumentException>(() => operation.ExecutePair(1, 1, _ => null), "双账号操作拒绝同一账号");
        UserState? escaped = null;
        operation.ExecutePair(2, 1, peer =>
        {
            Check.That(user.UserId == 2 && peer.Data.userRegistration!.userId == 1, "固定锁序不改变业务中的双方身份");
            using var nested = provider.CreateScope();
            Check.Throws<InvalidOperationException>(() => nested.ServiceProvider.GetRequiredService<UserOperation>()
                .ExecutePair(1, 2, _ => null), "跨作用域嵌套双账号操作拒绝");
            user.Data.userGamedata.coin = 120;
            peer.Data.userGamedata.coin = 110;
            escaped = peer;
            return null;
        });
        escaped!.Data.userGamedata.coin = 0;
        Check.That(store.Read(1)!.Data.userGamedata.coin == 110 && store.Read(2)!.Data.userGamedata.coin == 120,
            "双账号共同提交并隔离逃逸引用");
    }

    private static void VerifyConcurrentPairs()
    {
        var store = new MemoryUserStore();
        store.Save(1, TestUsers.Create(1, 0));
        store.Save(2, TestUsers.Create(2, 0));
        using var provider = TestUsers.Provider(store);
        using var start = new ManualResetEventSlim();
        var tasks = new[] { 1L, 2L }.Select(id => Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < 30; i++)
            {
                using var scope = provider.CreateScope();
                var user = scope.ServiceProvider.GetRequiredService<UserSession>();
                scope.ServiceProvider.GetRequiredService<UserOperation>().ExecutePair(id, 3 - id, peer =>
                {
                    user.Data.userGamedata.coin++;
                    peer.Data.userGamedata.coin++;
                    return null;
                });
            }
        })).Append(Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < 30; i++)
            {
                using var scope = provider.CreateScope();
                var user = scope.ServiceProvider.GetRequiredService<UserSession>();
                scope.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () => { user.Data.userGamedata.coin++; return null; });
            }
        })).ToArray();
        start.Set();
        Check.That(Task.WaitAll(tasks, TimeSpan.FromSeconds(10)), "相反顺序的双账号操作与单账号操作无死锁");
        Check.That(store.Read(1)!.Data.userGamedata.coin == 90 && store.Read(2)!.Data.userGamedata.coin == 60,
            "并发单账号与双账号操作不丢更新");
    }

    private static void VerifyMemoryBatchValidation()
    {
        var store = new MemoryUserStore();
        store.Save(1, TestUsers.Create(1));
        var first = store.Read(1)!;
        first.Data.userGamedata.coin = 0;
        Check.Throws<InvalidOperationException>(() => store.SaveMany(new Dictionary<long, UserState>
        {
            [1] = first, [2] = TestUsers.Create(3)
        }), "批量保存验证全部用户身份");
        Check.That(store.Read(1)!.Data.userGamedata.coin == 100 && store.Read(2) == null,
            "内存批量保存后项失败不提交前项");
    }

    private static void VerifyDatabaseRollback(SqliteUserStore store, string path)
    {
        using var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        sql.Open();
        using var trigger = sql.CreateCommand();
        trigger.CommandText = "CREATE TRIGGER fail_second BEFORE UPDATE ON user_blocks WHEN new.user_id=2 BEGIN SELECT RAISE(ABORT, 'fixture'); END;";
        trigger.ExecuteNonQuery();
        var first = store.Read(1)!;
        var second = store.Read(2)!;
        first.Data.userGamedata.coin++;
        second.Data.userGamedata.coin++;
        var batch = new Dictionary<long, UserState> { [1] = first, [2] = second };
        Check.Throws<SqliteException>(() => store.SaveMany(batch), "第二账号数据库写入失败");
        Check.That(store.Read(1)!.Data.userGamedata.coin == 110 && store.Read(2)!.Data.userGamedata.coin == 120,
            "数据库失败回滚第一账号、第二账号和缓存");
        using (var independent = new SqliteUserStore(path))
            Check.That(independent.Read(1)!.Data.userGamedata.coin == 110 && independent.Read(2)!.Data.userGamedata.coin == 120,
                "独立连接未观察到部分提交");
        trigger.CommandText = "DROP TRIGGER fail_second";
        trigger.ExecuteNonQuery();
        using (var independent = new SqliteUserStore(path))
        {
            var updated = independent.Read(2)!;
            updated.Data.userGamedata.coin++;
            independent.Save(2, updated);
        }
        Check.Throws<InvalidOperationException>(() => store.SaveMany(batch), "第二账号跨实例版本冲突拒绝整批");
        Check.That(store.Read(1)!.Data.userGamedata.coin == 110 && store.Read(2)!.Data.userGamedata.coin == 121,
            "版本冲突不留下第一账号的更新");
        batch[2] = store.Read(2)!;
        store.SaveMany(batch);
        Check.That(store.Read(1)!.Data.userGamedata.coin == 111 && store.Read(2)!.Data.userGamedata.coin == 121,
            "回滚未污染第一账号版本，重新读取冲突账号后可提交");
    }
}
