extern alias game;

using System;
using System.IO;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Live;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class ChallengePlayDayChecks
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 10, 12, 0, 0, TimeSpan.FromHours(9));
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/challenge-day-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "configs.json"), """
            [{"configKey":"date_change_hour","value":"4"},
             {"configKey":"challenge_live_reset_play_days_day_of_week","value":"MONDAY"}]
            """);
        File.WriteAllText(Path.Combine(directory, "challengeLivePlayDayRewardPeriods.json"), """
            [{"id":1,"priority":2,"startAt":0,"endAt":2000000000000,"challengeLivePlayDayRewards":[{"id":1,"playDays":1,"resourceBoxId":1}]},
             {"id":2,"priority":1,"startAt":1,"endAt":1900000000000,"challengeLivePlayDayRewards":[{"id":2,"playDays":1,"resourceBoxId":2}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
            [{"id":1,"resourceBoxPurpose":"challenge_live_play_day_reward","details":[{"resourceType":"jewel","resourceQuantity":999}]},
             {"id":2,"resourceBoxPurpose":"challenge_live_stage","details":[{"resourceType":"jewel","resourceQuantity":50}]},
             {"id":2,"resourceBoxPurpose":"challenge_live_play_day_reward","details":[{"resourceType":"jewel","resourceQuantity":20}]}]
            """);
        var clock = new Clock();
        var store = new MemoryUserStore();
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton<IUserStore>(store)
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory)).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var service = scope.ServiceProvider.GetRequiredService<ChallengeLiveService>();
        var master = scope.ServiceProvider.GetRequiredService<LiveMasterQueries>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        Check.That(master.GetChallengePlayDayPeriod(1).id == 1 && master.GetChallengePlayDayPeriod(2).id == 2 &&
            master.GetChallengePlayDayPeriod(1900000000000).id == 1,
            "出勤奖励期排除起止边界，按 priority 升序选择");
        void Reset() => store.Save(1, TestUsers.Create(1));
        void Receive(long start) => operations.Execute(1, () =>
        {
            service.RecordFirstPlayDay(start);
            return user.BuildRefresh();
        });
        Reset();
        var start = clock.Now.AddMinutes(-3).ToUnixTimeMilliseconds();
        Receive(start);
        var day = store.Read(1)!.Data.userChallengeLivePlayDay;
        Check.That(day.playDays == 1 && day.challengeLivePlayDayRewardStatus == "received" && day.lastPlayStartAt == start &&
            day.playDaysResetAt == new DateTimeOffset(2026, 1, 12, 4, 0, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds(),
            "首次出勤记录开局时间及下周一日切点");
        Check.That(store.Read(1)!.Data.userChargedCurrency.free == 20, "首次出勤按奖励期和资源盒用途发奖");
        Check.Throws<NotSupportedException>(() => Receive(start), "后续出勤规则未核验时不重复领取");
        Check.That(store.Read(1)!.Data.userChargedCurrency.free == 20, "拒绝重复出勤不多发奖励");
        Reset();
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            service.RecordFirstPlayDay(start);
            return new BrokenResponse();
        }), "首次出勤编码失败");
        Check.That(store.Read(1)!.Data.userChallengeLivePlayDay == null && store.Read(1)!.Data.userChargedCurrency == null,
            "出勤状态与奖励同时回滚");
        clock.Now = new DateTimeOffset(2026, 1, 12, 4, 0, 0, TimeSpan.FromHours(9));
        Check.Throws<NotSupportedException>(() => Receive(clock.Now.AddMinutes(-3).ToUnixTimeMilliseconds()),
            "跨日切结算暂不推测归属");
        Receive(clock.Now.ToUnixTimeMilliseconds());
        Check.That(store.Read(1)!.Data.userChallengeLivePlayDay.playDaysResetAt == clock.Now.AddDays(7).ToUnixTimeMilliseconds(),
            "周一日切点之后归入下一周重置");
    }
}
