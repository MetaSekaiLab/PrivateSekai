extern alias game;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Shop;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class MaterialExchangeLimitChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "material-limit-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string value) => File.WriteAllText(Path.Combine(directory, name + ".json"), value);
        Table("materialExchanges", """
            [{"id":44,"materialExchangeSummaryId":1,"resourceBoxId":18,"refreshCycle":"monthly","exchangeLimit":15,
              "costs":[{"costGroupId":1,"resourceType":"material","resourceId":15,"quantity":300}]},
             {"id":45,"materialExchangeSummaryId":1,"resourceBoxId":19,"refreshCycle":"monthly","exchangeLimit":15,
              "costs":[{"costGroupId":1,"resourceType":"material","resourceId":15,"quantity":100}]},
             {"id":46,"materialExchangeSummaryId":1,"resourceBoxId":20,"refreshCycle":"none","exchangeLimit":2,
              "costs":[{"costGroupId":1,"resourceType":"material","resourceId":15,"quantity":10}]},
             {"id":47,"materialExchangeSummaryId":1,"resourceBoxId":21,"refreshCycle":"monthly","exchangeLimit":2,
              "costs":[{"costGroupId":1,"resourceType":"material","resourceId":15,"quantity":10}]},
             {"id":48,"materialExchangeSummaryId":1,"resourceBoxId":22,"refreshCycle":"monthly","exchangeLimit":2,
              "costs":[{"costGroupId":1,"resourceType":"material","resourceId":15,"quantity":10}]}]
            """);
        Table("materialExchangeSummaries", """[{"id":1,"materialExchangeType":"normal"}]""");
        Table("resourceBoxes", """
            [{"id":18,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"material","resourceId":57,"resourceQuantity":1}]},
             {"id":19,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"virtual_coin","resourceQuantity":300}]},
             {"id":20,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"practice_ticket","resourceId":1,"resourceQuantity":3}]},
             {"id":21,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"material","resourceId":57,"resourceQuantity":1},{"resourceType":"virtual_coin","resourceQuantity":1}]},
             {"id":22,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"material","resourceId":0,"resourceQuantity":1}]}]
            """);
        var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
        var clock = new Clock();
        var store = new MemoryUserStore();
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<TimeProvider>(clock)
            .AddSingleton<IUserStore>(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var service = scope.ServiceProvider.GetRequiredService<ShopService>();
        long At(string value) => DateTimeOffset.Parse(value).ToUnixTimeMilliseconds();
        void Reset()
        {
            var state = TestUsers.Create(1);
            state.Data.userMaterials = [new() { materialId = 15, quantity = 10000 }];
            state.Data.userMaterialExchanges = [];
            state.Data.userPracticeTickets = [];
            store.Save(1, state);
            clock.Now = At("2026-10-20T10:00:00+09:00");
        }
        int Exchange(int id, int count = 1)
        {
            var status = 0;
            operation.Execute(1, () => {
                status = service.ExchangeMaterial(id, 1, count);
                return status == 200 ? new UserExchangeResponse { updatedResources = user.BuildRefresh() } : null;
            });
            return status;
        }
        Reset();
        Check.That(Exchange(44, 2) == 200, "月限材料可批量兑换");
        var saved = store.Read(1)!.Data;
        var record = saved.userMaterialExchanges.Single();
        Check.That(saved.userMaterials.Single(m => m.materialId == 15).quantity == 9400 && saved.userMaterials.Single(m => m.materialId == 57).quantity == 2
            && record.exchangeCount == 2 && record.totalExchangeCount == 2 && record.exchangeRemaining == 13
            && record.refreshedAt == At("2026-11-01T00:00:00+09:00") && record.lastExchangedAt == clock.Now, "月限按主表扣发、累计和保存下月零点");
        Check.That(Exchange(44, 13) == 200 && store.Read(1)!.Data.userMaterialExchanges.Single().exchangeStatus == "not_exchangeable"
            && store.Read(1)!.Data.userMaterialExchanges.Single().exchangeRemaining == 0, "正好用完限额即售罄");
        var full = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.That(Exchange(44) == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(full), "超限不扣材、不发奖也不改计数");
        clock.Now = At("2026-10-31T23:59:59.999+09:00");
        Check.That(Exchange(44) == 409, "月末最后一毫秒仍沿用旧限额");
        clock.Now++;
        Check.That(Exchange(44) == 200, "下月零点无需先读Suite即可重新兑换");
        record = store.Read(1)!.Data.userMaterialExchanges.Single();
        Check.That(record.exchangeCount == 1 && record.totalExchangeCount == 16 && record.exchangeRemaining == 14
            && record.refreshedAt == At("2026-12-01T00:00:00+09:00"), "跨月只重置本期计数，累计保留");
        Reset();
        Check.That(Exchange(45, 15) == 200 && store.Read(1)!.Data.userGamedata.virtualCoin == 4500, "月限虚拟货币逐次累加并扣完整材料费用");
        var beforeTime = store.Read(1)!.Data.userMaterialExchanges.Single().lastExchangedAt;
        clock.Now = At("2027-02-01T00:00:00+09:00");
        var suite = DumpSerializer.Deserialize<SuiteUser>(operation.Execute(1, () => user.BuildSuite()));
        record = suite.userMaterialExchanges.Single();
        Check.That(record.exchangeCount == 0 && record.totalExchangeCount == 15 && record.exchangeRemaining == 15
            && record.exchangeStatus == "exchangeable" && record.refreshedAt == At("2027-03-01T00:00:00+09:00")
            && record.lastExchangedAt == beforeTime && suite.userGamedata.virtualCoin == 4500, "跳过多月后Suite恢复限额，保留历史时间、总数与库存");
        var normalized = DumpSerializer.Serialize(store.Read(1)!.Data);
        operation.Execute(1, () => user.BuildSuite());
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(normalized), "同月重复刷新不清空当期状态");
        foreach (var month in Enumerable.Range(1, 12))
        {
            Reset();
            clock.Now = new DateTimeOffset(2028, month, 1, 0, 0, 0, TimeSpan.FromHours(9)).AddMilliseconds(-1).ToUnixTimeMilliseconds();
            Exchange(44, 15);
            clock.Now++;
            Check.That(Exchange(44) == 200 && store.Read(1)!.Data.userMaterialExchanges.Single().exchangeCount == 1,
                "全年月切含闰年二月与跨年均按日本零点恢复");
        }
        Reset();
        Exchange(46, 2);
        clock.Now = At("2027-01-01T00:00:00+09:00");
        operation.Execute(1, () => user.BuildSuite());
        Check.That(Exchange(46) == 409 && store.Read(1)!.Data.userMaterialExchanges.Single().totalExchangeCount == 2,
            "一次性限量不随月切恢复");
        Reset();
        Exchange(44, 15);
        var expired = store.Read(1)!;
        expired.Data.userMaterials = [new() { materialId = 15, quantity = 0 }];
        store.Save(1, expired);
        clock.Now = At("2026-11-01T00:00:00+09:00");
        full = DumpSerializer.Serialize(expired.Data);
        Check.That(Exchange(44) == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(full), "月切后余额不足仍不提交隐含重置");
        Reset();
        var overflow = store.Read(1)!;
        overflow.Data.userGamedata.virtualCoin = int.MaxValue;
        store.Save(1, overflow);
        full = DumpSerializer.Serialize(overflow.Data);
        Check.Throws<OverflowException>(() => Exchange(47), "后项虚拟币库存溢出");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(full), "后项失败回滚首项材料、费用及月度记录");
        Reset();
        full = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<InvalidOperationException>(() => Exchange(48), "零编号材料奖励不能静默丢失后返回成功");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(full), "无效奖励保留完整用户状态");
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () => {
            service.ExchangeMaterial(44, 1, 1); return new GachaExtraChecks.BrokenResponse();
        }), "月度兑换后响应编码失败");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(full), "编码失败回滚扣发和限额");
        Reset();
        var legacy = store.Read(1)!;
        legacy.Data.userMaterialExchanges = [new() { materialExchangeId = 44, exchangeCount = 14, totalExchangeCount = 40 }];
        store.Save(1, legacy);
        operation.Execute(1, () => user.BuildSuite());
        record = store.Read(1)!.Data.userMaterialExchanges.Single();
        Check.That(record.exchangeCount == 14 && record.totalExchangeCount == 40 && record.exchangeRemaining == 1
            && record.refreshedAt == At("2026-11-01T00:00:00+09:00"), "补齐缺少截止时间的旧记录时保留已用次数");
        Check.That(Exchange(44, 2) == 409 && Exchange(44) == 200, "缺元数据的记录不能额外获得完整月限");
        legacy = store.Read(1)!;
        legacy.Data.userMaterialExchanges.Single().totalExchangeCount = int.MaxValue;
        legacy.Data.userMaterialExchanges.Single().refreshedAt = 1;
        store.Save(1, legacy);
        full = DumpSerializer.Serialize(legacy.Data);
        Check.Throws<OverflowException>(() => Exchange(44), "跨月累计次数溢出仍拒绝");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(full), "累计溢出不提交月切、费用或奖励");
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () => {
            user.BuildSuite(); return new GachaExtraChecks.BrokenResponse();
        }), "Suite月度刷新后编码失败");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(full), "Suite编码失败保留原本售罄及截止状态");
        Reset();
        var statuses = Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => {
            using var race = provider.CreateScope();
            var result = 0;
            race.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () => {
                result = race.ServiceProvider.GetRequiredService<ShopService>().ExchangeMaterial(44, 1, 10); return null;
            });
            return result;
        }))).GetAwaiter().GetResult();
        Check.That(statuses.Order().SequenceEqual(new[] { 200, 409 }) && store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 57).quantity == 10,
            "并发兑换串行校验同一月限，不超发或多扣");
        var database = Path.Combine(directory, "material-limits.db");
        using (var sqlite = new SqliteUserStore(database)) sqlite.Save(1, store.Read(1)!);
        using var restored = new SqliteUserStore(database);
        clock.Now = At("2026-11-01T00:00:00+09:00");
        using var resumed = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<TimeProvider>(clock)
            .AddSingleton<IUserStore>(restored).BuildServiceProvider();
        using var resumedScope = resumed.CreateScope();
        resumedScope.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () => resumedScope.ServiceProvider.GetRequiredService<UserSession>().BuildSuite());
        record = restored.Read(1)!.Data.userMaterialExchanges.Single();
        Check.That(record.exchangeCount == 0 && record.totalExchangeCount == 10 && record.exchangeRemaining == 15, "SQLite重启后月切仍恢复限额并保留累计");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "月度兑换与刷新不改其他账号");
    }

    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
}
