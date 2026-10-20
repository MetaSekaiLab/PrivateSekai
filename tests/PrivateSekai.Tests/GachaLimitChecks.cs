extern alias game;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

internal static class GachaLimitChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "gacha-limit-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string json) => File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        Table("gachas", """
            [{"id":1,"startAt":1,"gachaCeilItemId":9,"gachaDetails":[{"cardId":2,"weight":1}],
              "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100}],
              "gachaBehaviors":[{"id":1,"spinCount":1,"executeLimit":2,"gachaBehaviorType":"normal","costResourceType":"jewel","costResourceQuantity":300},
                {"id":2,"spinCount":1,"executeLimit":1,"gachaBehaviorType":"once_a_day","costResourceType":"paid_jewel","costResourceQuantity":100},
                {"id":3,"spinCount":1,"executeLimit":1,"gachaBehaviorType":"once_a_day","gachaSpinnableType":"colorful_pass","resourceCategory":"free_resource"},
                {"id":4,"spinCount":1,"executeLimit":1,"gachaBehaviorType":"once_a_week","gachaSpinnableType":"colorful_pass","resourceCategory":"free_resource"},
                {"id":5,"spinCount":1,"executeLimit":1,"gachaBehaviorType":"normal","costResourceType":"jewel","costResourceQuantity":300}]},
             {"id":2,"startAt":1,"gachaDetails":[{"cardId":2,"weight":1}],
              "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100}],
              "gachaBehaviors":[{"id":3,"spinCount":1,"executeLimit":1,"gachaBehaviorType":"once_a_day","gachaSpinnableType":"colorful_pass","resourceCategory":"free_resource"},
                {"id":4,"spinCount":1,"executeLimit":1,"gachaBehaviorType":"once_a_week","gachaSpinnableType":"colorful_pass","resourceCategory":"free_resource"}]},
             {"id":3,"startAt":1,"spinLimit":3,"gachaDetails":[{"cardId":2,"weight":1}],
              "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100}],
              "gachaBehaviors":[{"id":1,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300},
                {"id":10,"spinCount":10,"costResourceType":"jewel","costResourceQuantity":3000}]}]
            """);
        Table("cards", """[{"id":2,"characterId":1,"cardRarityType":"rarity_2"}]""");
        foreach (var name in new[] { "cardEpisodes", "cardCostume3ds", "characterMissionV2s", "characterMissionV2ParameterGroups", "honorMissions", "beginnerMissionV2s" }) Table(name, "[]");
        Table("configs", """[{"configKey":"date_change_hour","value":"4"}]""");
        Table("colorfulPassV2s", """[{"id":1,"dailyPaidGachaSpinLimit":2,"dailyFreeGachaSpinLimit":0,"weeklyColorfulPassGachaSpinLimit":1},{"id":3,"dailyPaidGachaSpinLimit":2,"dailyFreeGachaSpinLimit":1,"weeklyColorfulPassGachaSpinLimit":1}]""");
        var clock = new Clock();
        var store = new MemoryUserStore();
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<IUserStore>(store).AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        long At(string date) => DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        void Reset(int pass = 0)
        {
            clock.Now = At("2026-10-18T12:00:00+09:00");
            var state = TestUsers.Create(1);
            state.Data.userChargedCurrency = new() { free = 10000, paid = 10000 };
            state.Data.userColorfulPassV2 = pass == 0 ? null : new() { colorfulPassId = pass, expiredAt = checked((ulong)(clock.Now + 30 * 86400000L)) };
            state.Data.refreshableTypes = ["userMaterials"];
            store.Save(1, state);
        }
        UserGachaResponse Draw(int behavior, int pool = 1, int? executions = null) => DumpSerializer.Deserialize<UserGachaResponse>(operation.Execute(1, () =>
        {
            var response = gacha.ExecuteGacha(pool, behavior, false, executions);
            response.updatedResources = user.BuildRefresh(prepareRefresh: false);
            return response;
        }));
        void Reject(int behavior, int pool = 1, int? executions = null)
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            var error = Check.Throws<GachaRequestRejectedException>(() => Draw(behavior, pool, executions), "已用完的抽卡名额拒绝");
            Check.That(error.Status == 409 && error.Code == "" && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "名额拒绝保留全部货币、卡牌、计数、免费时间及待刷新字段");
        }
        Reset();
        Draw(1);
        Draw(1);
        Reject(1);
        Draw(5);
        Reject(5);
        Check.That(store.Read(1)!.Data.userGachas.Single(g => g.gachaBehaviorId == 1).count == 2 &&
            store.Read(1)!.Data.userGachas.Single(g => g.gachaBehaviorId == 5).count == 1, "普通行为按各自执行次数限额，不能因同池其他按钮而恢复名额");
        Reset();
        Draw(1, 3);
        Draw(1, 3);
        Reject(10, 3);
        Draw(1, 3);
        Reject(1, 3);
        Check.That(store.Read(1)!.Data.userGachas.Single().count == 3, "整池上限按实际抽数累计，不能用十连越过剩余单抽名额");
        Reset();
        Draw(1, 3);
        var partial = Draw(1, 3, 2);
        Check.That(partial.obtainPrizes.Length == 2 && partial.userGacha.count == 3 &&
            store.Read(1)!.Data.userChargedCurrency.free == 9100 && store.Read(1)!.Data.userCards.Single().duplicateCount == 2,
            "限量池剩余两抽用单抽行为指定两次，按两份费用发两张卡，计数累计到三");
        Reject(1, 3);
        Reset();
        Reject(1, executions: 3);
        Reject(1, executions: int.MaxValue);
        foreach (var count in new[] { 0, -1 })
        {
            var invalid = Check.Throws<GachaRequestRejectedException>(() => Draw(1, executions: count), "非正或超额指定抽数拒绝");
            Check.That(invalid.Status == 400 && store.Read(1)!.Data.userChargedCurrency.free == 10000,
                "无效指定抽数不扣资源或修改计数");
        }
        Reset();
        Draw(2);
        Reject(2);
        var last = store.Read(1)!.Data.userGachas.Single().lastSpinAt;
        clock.Now = At("2026-10-19T03:59:59.999+09:00");
        Reject(2);
        clock.Now = At("2026-10-19T04:00:00+09:00");
        var suite = DumpSerializer.Deserialize<SuiteUser>(operation.Execute(1, () => user.BuildSuite()));
        Check.That(suite.userGachas.Single().count == 0 && suite.userGachas.Single().lastSpinAt == last,
            "日切Suite恢复每日付费次数，保留最后实际抽卡时间");
        Check.That(Draw(2).userGacha.count == 1 && store.Read(1)!.Data.userChargedCurrency.paid == 9800,
            "日切后再次付费从一计数，未提前扣款或追发奖励");
        Reset(1);
        Draw(2);
        Draw(2);
        Reject(2);
        Reject(3);
        Check.That(store.Read(1)!.Data.userChargedCurrency.paid == 9800, "通行证付费次数读取该等级master，每日免费资格独立判断");
        Reset(3);
        Reject(3, executions: 2);
        var first = Draw(3);
        Reject(3, 2);
        Check.That(first.obtainPrizes.Length == 1 && first.obtainGachaCeilItems.Length == 0 && first.consumedCosts.Length == 0 &&
            store.Read(1)!.Data.userChargedCurrency.free == 10000 && store.Read(1)!.Data.userGachaFreeResources.Single().gachaBehaviorType == "once_a_day",
            "通行证每日免费跨池共用次数，发一张卡，不扣款、不发天井，记录真实使用时间");
        Draw(4);
        Reject(4, 2);
        clock.Now = At("2026-10-19T04:00:00+09:00");
        Reject(4, 2);
        Draw(3, 2);
        clock.Now += 1000;
        Check.That(Draw(4, 2).userGacha.count == 1 && store.Read(1)!.Data.userGachaFreeResources.Length == 2,
            "日次与周次免费记录分开；周次遵循客户端周一日切后的严格时间比较");
        Reset(3);
        var expired = store.Read(1)!;
        expired.Data.userColorfulPassV2.expiredAt = checked((ulong)clock.Now);
        store.Save(1, expired);
        Draw(3);
        clock.Now++;
        Reject(4);
        Reset(3);
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            gacha.ExecuteGacha(1, 3, false);
            return new BrokenResponse();
        }), "免费抽卡已发奖及记录名额后编码失败");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "响应失败整体回滚免费抽卡名额、卡牌和任务，不妨碍下一次真实抽取");
        Draw(3);
        var state = store.Read(1)!;
        var path = Path.Combine(directory, "users.db");
        using (var sql = new SqliteUserStore(path)) sql.Save(1, state);
        using (var sql = new SqliteUserStore(path))
            Check.That(sql.Read(1)!.Data.userGachaFreeResources.Single().lastSpinAt == state.Data.userGachaFreeResources.Single().lastSpinAt &&
                sql.Read(1)!.Data.userGachas.Single().count == 1, "数据库重启保留免费名额时间与行为记录");
        Reset();
        var accepted = 0;
        var rejected = 0;
        Parallel.For(0, 2, _ =>
        {
            using var concurrent = provider.CreateScope();
            try
            {
                concurrent.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () =>
                    concurrent.ServiceProvider.GetRequiredService<GachaService>().ExecuteGacha(1, 5, false));
                Interlocked.Increment(ref accepted);
            }
            catch (GachaRequestRejectedException) { Interlocked.Increment(ref rejected); }
        });
        Check.That(accepted == 1 && rejected == 1 && store.Read(1)!.Data.userChargedCurrency.free == 9700 &&
            store.Read(1)!.Data.userGachas.Single().count == 1, "并发争用最后一次名额只有一个请求扣费发奖并提交");
    }

    private sealed class Clock : TimeProvider
    {
        public long Now { get; set; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
}
