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

internal static class MemoriaExchangeChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "memoria-exchange-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string value) => File.WriteAllText(Path.Combine(directory, name + ".json"), value);
        Table("materialExchangeSummaries", """[{"id":21,"materialExchangeType":"normal"}]""");
        Table("materialExchanges", """
            [{"id":1,"materialExchangeSummaryId":21,"resourceBoxId":1,"refreshCycle":"none",
              "costs":[{"costGroupId":1,"resourceType":"material","resourceId":168,"quantity":1},
               {"costGroupId":2,"resourceType":"mysekai_material","resourceId":35,"quantity":1},
               {"costGroupId":3,"resourceType":"mysekai_material","resourceId":36,"quantity":2},
               {"costGroupId":4,"resourceType":"material","resourceId":35,"quantity":2},
               {"costGroupId":4,"resourceType":"mysekai_material","resourceId":35,"quantity":3},
               {"costGroupId":4,"resourceType":"mysekai_material","resourceId":35,"quantity":2},
               {"costGroupId":5,"resourceType":"mysekai_item","resourceId":9,"quantity":1},
               {"costGroupId":6,"resourceType":"mysekai_material","resourceId":1,"quantity":2}],
              "materialExchangeRelationParents":[{"id":777,"materialExchangeRelationChildren":[
               {"materialExchangeId":1,"materialExchangeCostGroupId":2},{"materialExchangeId":1,"materialExchangeCostGroupId":3}]}]},
             {"id":2,"materialExchangeSummaryId":21,"resourceBoxId":2,"refreshCycle":"none",
              "costs":[{"costGroupId":1,"resourceType":"material","resourceId":168,"quantity":1},
               {"costGroupId":1,"resourceType":"mysekai_material","resourceId":35,"quantity":1}]},
             {"id":3,"materialExchangeSummaryId":21,"resourceBoxId":1,"refreshCycle":"none",
              "costs":[{"costGroupId":1,"resourceType":"material","resourceId":168,"quantity":1},
               {"costGroupId":1,"resourceType":"mysekai_material","resourceId":999,"quantity":1}]}]
            """);
        Table("mysekaiMaterials", """
            [{"id":1,"mysekaiMaterialType":"wood"},{"id":35,"mysekaiMaterialType":"game_character"},
             {"id":36,"mysekaiMaterialType":"game_character"},{"id":55,"mysekaiMaterialType":"game_character"}]
            """);
        Table("resourceBoxes", """
            [{"id":1,"resourceBoxPurpose":"other","details":[{"resourceType":"material","resourceId":99,"resourceQuantity":999}]},
             {"id":1,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"mysekai_material","resourceId":55,"resourceQuantity":1}]},
             {"id":2,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"mysekai_material","resourceId":55,"resourceQuantity":1},
              {"resourceType":"mysekai_material","resourceId":999,"resourceQuantity":1}]}]
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
        void Reset()
        {
            var state = TestUsers.Create(1);
            state.Data.userMaterials = [new() { materialId = 168, quantity = 10 }, new() { materialId = 35, quantity = 10 }];
            state.Data.userMysekaiMaterials = [new() { mysekaiMaterialId = 35, quantity = 10, lastObtainedAt = 80 }, new() { mysekaiMaterialId = 36, quantity = 10, lastObtainedAt = 90 }];
            state.Data.userMysekaiMaterialPossession = new() { quantity = 99 };
            state.Data.userMaterialExchanges = [];
            store.Save(1, state);
            clock.Now = 1000;
        }
        int Exchange(int group, int count = 1, int id = 1)
        {
            var code = 0;
            operation.Execute(1, () => {
                code = service.ExchangeMaterial(id, group, count);
                return code == 200 ? new UserExchangeResponse { updatedResources = user.BuildRefresh(prepareRefresh: false) } : null;
            });
            return code;
        }
        Reset();
        Check.That(Exchange(2, 2) == 200, "关联展示下选择具体Memoria消耗组可批量兑换");
        var saved = store.Read(1)!.Data;
        Check.That(saved.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 35).quantity == 8
            && saved.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 55).quantity == 2
            && saved.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 36).quantity == 10
            && saved.userMaterials.Single(m => m.materialId == 168).quantity == 10, "只扣选定替代资源，不扣未选组或误发同编号其他用途箱");
        clock.Now = 2000;
        Check.That(Exchange(1, 3) == 200 && store.Read(1)!.Data.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 55).quantity == 5,
            "七色材料与角色Memoria两种路径共用奖励库存");
        saved = store.Read(1)!.Data;
        Check.That(saved.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 35).lastObtainedAt == 80
            && saved.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 55).lastObtainedAt == 2000
            && saved.userMysekaiMaterialPossession.quantity == 99, "消耗保留获得时间，奖励更新时间，角色Memoria不改变普通仓库占用");
        Check.That(Exchange(2, 8) == 200 && store.Read(1)!.Data.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 35).quantity == 0,
            "耗尽仍保留零库存记录及原获得时间");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.That(Exchange(2) == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "所选组不足不自动改扣其他充足材料");
        Check.Throws<ArgumentException>(() => Exchange(777), "展示父组编号不能作为成本组编号");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "非法选择不改变任何库存或次数");
        Reset();
        Check.That(Exchange(4) == 200 && store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 35).quantity == 8
            && store.Read(1)!.Data.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 35).quantity == 5, "按类型及编号累计同组成本，同号不同资源不混合");
        Reset();
        var empty = store.Read(1)!;
        empty.Data.userMysekaiMaterials = [];
        empty.Data.userMysekaiMaterialPossession = null;
        store.Save(1, empty);
        before = DumpSerializer.Serialize(empty.Data);
        Check.That(Exchange(2) == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "缺少源Memoria按零库存拒绝");
        Check.That(Exchange(1) == 200 && store.Read(1)!.Data.userMysekaiMaterialPossession == null, "首次领取Memoria不凭空建立普通仓库占用");
        Reset();
        before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.That(Exchange(5) == 501 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "其他Mysekai资源消耗仍显式拒绝");
        Check.Throws<InvalidOperationException>(() => Exchange(1, id: 2), "整箱后项缺少Mysekai材料主表");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "后项失败回滚两种消耗、先前奖励、时间和计数");
        var missing = store.Read(1)!;
        missing.Data.userMysekaiMaterials = [new() { mysekaiMaterialId = 999, quantity = 10 }];
        store.Save(1, missing);
        before = DumpSerializer.Serialize(missing.Data);
        Check.Throws<InvalidOperationException>(() => Exchange(1, id: 3), "源材料存在库存但缺少主表仍拒绝");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "后项扣费失败回滚先前普通材料扣除");
        Reset();
        var overflow = store.Read(1)!;
        overflow.Data.userMysekaiMaterials = [new() { mysekaiMaterialId = 35, quantity = 10 }, new() { mysekaiMaterialId = 55, quantity = int.MaxValue }];
        store.Save(1, overflow);
        before = DumpSerializer.Serialize(overflow.Data);
        Check.Throws<OverflowException>(() => Exchange(2), "目标Memoria库存溢出");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "溢出回滚源库存、获得时间与次数");
        Reset();
        before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () => {
            service.ExchangeMaterial(1, 2, 2); return new GachaExtraChecks.BrokenResponse();
        }), "Memoria兑换后编码失败");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败回滚整个兑换");
        var counted = store.Read(1)!;
        counted.Data.userMysekaiMaterials = [new() { mysekaiMaterialId = 1, quantity = 5, lastObtainedAt = 80 }];
        store.Save(1, counted);
        Check.That(Exchange(6) == 200 && store.Read(1)!.Data.userMysekaiMaterialPossession.quantity == 97, "计入普通仓库的材料消耗同步减少总占用");
        counted = store.Read(1)!;
        counted.Data.userMysekaiMaterialPossession.quantity = 1;
        store.Save(1, counted);
        before = DumpSerializer.Serialize(counted.Data);
        Check.Throws<InvalidOperationException>(() => Exchange(6), "普通仓库占用不足不允许回绕");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "占用不一致时整次兑换不生效");
        Reset();
        var codes = Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => {
            using var race = provider.CreateScope();
            var code = 0;
            race.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () => {
                code = race.ServiceProvider.GetRequiredService<ShopService>().ExchangeMaterial(1, 2, 7); return null;
            });
            return code;
        }))).GetAwaiter().GetResult();
        Check.That(codes.Order().SequenceEqual(new[] { 200, 409 }) && store.Read(1)!.Data.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 55).quantity == 7,
            "并发兑换同一源库存仅一笔成功，不能透支或超发");
        var database = Path.Combine(directory, "memoria.db");
        using (var sqlite = new SqliteUserStore(database)) sqlite.Save(1, store.Read(1)!);
        using var restored = new SqliteUserStore(database);
        using var resumed = new ServiceCollection().AddPrivateSekai().AddSingleton(master).AddSingleton<TimeProvider>(clock)
            .AddSingleton<IUserStore>(restored).BuildServiceProvider();
        using var resumedScope = resumed.CreateScope();
        resumedScope.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () => resumedScope.ServiceProvider.GetRequiredService<ShopService>().ExchangeMaterial(1, 2, 3));
        saved = restored.Read(1)!.Data;
        Check.That(saved.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 35).quantity == 0 && saved.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 55).quantity == 10
            && saved.userMaterialExchanges.Single().totalExchangeCount == 10, "SQLite重启后继续兑换，库存与累计次数一致");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "Memoria兑换不改变其他账号");
    }

    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
}
