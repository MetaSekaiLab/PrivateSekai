extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Gacha;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class GachaExchangeChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "gacha-exchange-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "gachaCeilExchangeSummaries.json"), """
            [{"id":1,"startAt":900,"endAt":1100,"gachaCeilExchanges":[
              {"id":11,"startAt":900,"endAt":1100,"resourceBoxId":1,"gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","gachaCeilItemId":9,"quantity":1}},
              {"id":12,"startAt":900,"endAt":1100,"resourceBoxId":2,"exchangeLimit":2,"gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","resourceId":9,"quantity":2}},
              {"id":13,"startAt":900,"endAt":1100,"resourceBoxId":1,"substituteLimit":2,"gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","resourceId":9,"quantity":30},
               "gachaCeilExchangeSubstituteCosts":[{"id":1,"resourceType":"material","resourceId":48,"substituteQuantity":10}]},
              {"id":14,"startAt":1001,"endAt":1100,"resourceBoxId":1,"gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","resourceId":9,"quantity":1}},
              {"id":15,"startAt":900,"endAt":1100,"resourceBoxId":3,"gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","resourceId":9,"quantity":1}}]},
             {"id":2,"startAt":1001,"endAt":1200,"gachaCeilExchanges":[
              {"id":16,"startAt":900,"endAt":1100,"resourceBoxId":1,"gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","resourceId":9,"quantity":1}}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
            [{"id":1,"resourceBoxPurpose":"gacha_ceil_exchange","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":5}]},
             {"id":2,"resourceBoxPurpose":"gacha_ceil_exchange","details":[{"resourceType":"material","resourceId":48,"resourceLevel":1,"resourceQuantity":1}]},
             {"id":3,"resourceBoxPurpose":"gacha_ceil_exchange","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":5},{"resourceType":"unsupported","resourceId":1,"resourceQuantity":1}]}]
            """);
        var clock = new Clock { Now = 1000 };
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userGachaCeilItems = [new() { gachaCeilItemId = 9, quantity = 10 }];
        state.Data.userMaterials = [new() { materialId = 48, quantity = 2 }];
        state.Data.userGachaCeilExchanges = [];
        state.Data.refreshableTypes = ["userMaterials"];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<TimeProvider>(clock).AddSingleton<IUserStore>(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        UserGachaCeilExchangeRequest Request(int id, int count, int substitute = 0, int amount = 0) => new()
        {
            gachaCeilExchangeRequest = new() { gachaExchangeId = id, exchangeCount = count,
                gachaCeilExchangeSubstituteCostId = substitute, substituteCostCount = amount }
        };
        UserGachaCeilExchangeResponse Exchange(UserGachaCeilExchangeRequest request) =>
            DumpSerializer.Deserialize<UserGachaCeilExchangeResponse>(operation.Execute(1, () =>
            {
                var response = gacha.ExchangeGachaCeilItem(request);
                response.updatedResources = user.BuildRefresh();
                return response;
            }));
        void Reject(UserGachaCeilExchangeRequest request, int status, string code = "")
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            var error = Check.Throws<GachaRequestRejectedException>(() => Exchange(request), "非法天井兑换拒绝");
            Check.That(error.Status == status && error.Code == code && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "天井兑换拒绝保持全部库存、次数、其他字段及待刷新字段不变");
        }
        Reject(new(), 500);
        Reject(Request(0, 1), 404);
        Reject(Request(int.MaxValue, 1), 404);
        Reject(Request(11, 0), 400);
        Reject(Request(11, -1), 400);
        Reject(Request(12, 3), 409);
        Reject(Request(11, 11), 409, "not_enough_resource");
        Reject(Request(14, 1), 409, "invalid_term");
        Reject(Request(16, 1), 409, "invalid_term");
        clock.Now = 1100;
        Reject(Request(11, 1), 409, "invalid_term");
        clock.Now = 1000;
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<NotSupportedException>(() => Exchange(Request(15, 1)), "奖励中途失败回滚费用、前项材料与兑换记录");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "资源处理失败不保留部分兑换");
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            gacha.ExchangeGachaCeilItem(Request(12, 1));
            return new BrokenResponse();
        }), "天井兑换响应编码失败整体回滚");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "响应编码失败不消耗库存或剩余次数");
        clock.Now = 900;
        var first = Exchange(Request(12, 1));
        Check.That(first.obtainUserResources.Single().quantity == 1 && first.obtainUserResources.Single().resourceLevel == 1 &&
            store.Read(1)!.Data.userGachaCeilItems.Single().quantity == 8 &&
            store.Read(1)!.Data.userGachaCeilExchanges.Single().exchangeRemaining == 1,
            "限量材料在开放起点可兑换，按资源箱到账并扣除真实天井库存及一次限额");
        Exchange(Request(12, 1));
        Check.That(store.Read(1)!.Data.userGachaCeilExchanges.Single().exchangeRemaining == 0 &&
            store.Read(1)!.Data.userGachaCeilExchanges.Single().exchangeStatus == "not_exchangeable", "最后一次限量兑换保存售罄状态");
        Reject(Request(12, 1), 409);
        var batch = Exchange(Request(11, 6));
        Check.That(batch.obtainUserResources.Single().quantity == 30 && store.Read(1)!.Data.userGachaCeilItems.Single().quantity == 0 &&
            store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 15).quantity == 30 &&
            store.Read(1)!.Data.userMaterials.Select(m => m.materialId).SequenceEqual([15, 48]),
            "批量兑换按总数量一次扣发，库存刚好用完且返回总获得量");
        Reject(Request(11, 1), 409, "not_enough_resource");
        state = store.Read(1)!;
        state.Data.userGachaCeilItems.Single().quantity = 10;
        state.Data.userMaterials.Single(m => m.materialId == 48).quantity = 2;
        store.Save(1, state);
        var substitute = Exchange(Request(13, 1, 1, 2));
        Check.That(substitute.obtainUserResources.Single().quantity == 5 && store.Read(1)!.Data.userGachaCeilItems.Single().quantity == 0 &&
            store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 48).quantity == 0 &&
            store.Read(1)!.Data.userGachaCeilExchangeSubstituteCosts.Single().substituteCostUsedCount == 2,
            "替代票按客户端倍率减少主费用，票券只扣实际张数，并累计已使用限额");
        Reject(Request(13, 1, 1, 1), 409);
        var path = Path.Combine(directory, "users.db");
        using (var sql = new SqliteUserStore(path)) sql.Save(1, store.Read(1)!);
        using (var sql = new SqliteUserStore(path))
            Check.That(sql.Read(1)!.Data.userMaterials.Single(m => m.materialId == 15).quantity == 35 &&
                sql.Read(1)!.Data.userGachaCeilExchanges.Single(e => e.gachaCeilExchangeId == 12).exchangeRemaining == 0 &&
                sql.Read(1)!.Data.userGachaCeilExchangeSubstituteCosts.Single().substituteCostUsedCount == 2,
                "数据库重启保留材料、售罄状态与替代票使用次数");
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "指定数量兑换不修改其他账号");
    }

    private sealed class Clock : TimeProvider
    {
        public long Now { get; set; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
}
