extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Live;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class BoostRecoveryChecks
{
    private const long Now = 10_000_000;
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }

    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/boost-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "boostItems.json"), """[{"id":1,"recoveryValue":1}]""");
        File.WriteAllText(Path.Combine(directory, "configs.json"), """
            [{"configKey":"boost_recovery_max_count","value":"25"},
             {"configKey":"boost_recovery_second","value":"1800"},
             {"configKey":"boost_max_count","value":"999"}]
            """);
        var store = new MemoryUserStore();
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton<IUserStore>(store)
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<TimeProvider>(new Clock()).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var live = scope.ServiceProvider.GetRequiredService<LiveService>();
        var boosts = scope.ServiceProvider.GetRequiredService<BoostService>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var request = new UserBoostItemRequest { costs = [new() { resourceId = 1, resourceType = "boost_item", quantity = 1 }] };
        void Reset(int current, int quantity, ulong recoveryAt = Now - 1000)
        {
            var state = TestUsers.Create(1);
            state.Data.userBoost = new() { current = current, recoveryAt = recoveryAt };
            state.Data.userBoostItems = [new() { userId = 1, boostItemId = 1, quantity = quantity }];
            store.Save(1, state);
        }
        int Execute()
        {
            var status = 0;
            operations.Execute(1, () => { status = live.RecoverBoost(request); return user.BuildRefresh(); });
            return status;
        }
        Reset(15, 1);
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            live.RecoverBoost(request);
            return new BrokenResponse();
        }), "体力恢复编码失败");
        Check.That(store.Read(1)!.Data.userBoost.current == 15 && store.Read(1)!.Data.userBoostItems.Single().quantity == 1,
            "体力恢复编码失败同时回滚库存和体力");
        Check.That(Execute() == 200 && store.Read(1)!.Data.userBoost.current == 16 &&
            store.Read(1)!.Data.userBoost.recoveryAt == Now - 1000 && store.Read(1)!.Data.userBoostItems.Single().quantity == 0,
            "低于自然上限保留恢复计时，耗尽道具保留零库存条目");
        Check.Throws<ArgumentException>(() => Execute(), "库存不足不能再次恢复");
        Reset(24, 2);
        Check.That(Execute() == 200 && store.Read(1)!.Data.userBoost.current == 25 &&
            store.Read(1)!.Data.userBoost.recoveryAt == Now, "到达自然上限重置恢复时间");
        Reset(25, 1);
        Check.That(Execute() == 200 && store.Read(1)!.Data.userBoost.current == 26 &&
            store.Read(1)!.Data.userBoost.recoveryAt == Now && store.Read(1)!.Data.userBoostItems.Single().quantity == 0,
            "道具恢复可超过自然上限，正常扣材并更新时间");
        Reset(15, 1, Now - 1_800_000);
        Check.That(Execute() == 200 && store.Read(1)!.Data.userBoost.current == 17 &&
            store.Read(1)!.Data.userBoost.recoveryAt == Now && store.Read(1)!.Data.userBoostItems.Single().quantity == 0,
            "先结算到期自然恢复，再应用道具恢复");
        Reset(999, 1);
        Check.That(Execute() == 501 && store.Read(1)!.Data.userBoostItems.Single().quantity == 1,
            "超过体力硬上限不猜测截断行为或扣材");
        Reset(16, 0, Now - 1_800_123);
        for (var i = 0; i < 2; i++)
            operations.Query(1, () =>
            {
                boosts.Normalize();
                Check.That(user.Data.userBoost.current == 17 && user.Data.userBoost.recoveryAt == Now - 123,
                    "自然恢复只累计完整周期并保留剩余时间，重复查询不重复发放");
                return user.BuildSuite();
            });
        Check.That(store.Read(1)!.Data.userBoost.current == 16,
            "只读恢复使用隔离投影，不保存查询副本");
        operations.Execute(1, () => { boosts.Consume(5); return user.BuildRefresh(); });
        Check.That(store.Read(1)!.Data.userBoost.current == 12 && store.Read(1)!.Data.userBoost.recoveryAt == Now - 123,
            "扣体力前结算自然恢复，低于上限消耗不重置计时");
        Reset(24, 0, Now - 3_600_123);
        operations.Query(1, () =>
        {
            boosts.Normalize();
            Check.That(user.Data.userBoost.current == 25 && user.Data.userBoost.recoveryAt == Now,
                "自然恢复受上限约束，满体力时停止积累旧时间");
            return user.BuildSuite();
        });
    }
}
