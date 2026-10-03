extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Home;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class LoginBonusChecks
{
    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/login-bonus-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "configs.json"), """[{"configKey":"login_bonus_id","value":"1"},{"configKey":"beginner_login_bonus_enable","value":"true"}]""");
            File.WriteAllText(Path.Combine(directory, "loginBonuses.json"), """[{"id":1,"day":1,"resourceBoxId":1}]""");
            File.WriteAllText(Path.Combine(directory, "beginnerLoginBonusSummaries.json"), """[{"id":3,"loginBonusId":3,"startAt":1,"endAt":4102412399000}]""");
            File.WriteAllText(Path.Combine(directory, "beginnerLoginBonuses.json"), """[{"id":27,"loginBonusId":3,"day":1,"resourceBoxId":2}]""");
            File.WriteAllText(Path.Combine(directory, "limitedLoginBonuses.json"), "[]");
            // 隔离夹具验证奖励来自 master，不使用正式数值。
            File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
                [{"id":1,"resourceBoxPurpose":"login_bonus","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":2}]},
                 {"id":2,"resourceBoxPurpose":"login_bonus","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":3}]}]
                """);
            var store = new MemoryUserStore();
            using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton<IUserStore>(store)
                .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory)).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var bonuses = scope.ServiceProvider.GetRequiredService<LoginBonusService>();
            var initial = TestUsers.Create(1);
            initial.Data.userLoginBonuses = [];
            initial.Data.userHonorMissions = [];
            initial.Data.userPresents = [];
            initial.Data.userMaterials = [];
            store.Save(1, initial);
            Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
            {
                bonuses.ClaimInitial();
                return new BrokenResponse();
            }), "登录奖励响应编码失败时整个操作回滚");
            Check.That(store.Read(1)!.Data.userPresents.Count == 0 && store.Read(1)!.Data.userLoginBonuses.Length == 0 &&
                store.Read(1)!.Data.userHonorMissions.Length == 0,
                "失败刷新不遗留邮箱、登录记录或荣誉进度");
            var first = DumpSerializer.Deserialize<UserHomeRefreshResponse>(operations.Execute(1, () =>
            {
                var claimed = bonuses.ClaimInitial();
                return new UserHomeRefreshResponse { userLoginBonuses = claimed, updatedResources = user.BuildRefresh() };
            }));
            var saved = store.Read(1)!.Data;
            Check.That(first.userLoginBonuses.Select(b => b.loginBonusType).SequenceEqual(["normal", "beginner"]) &&
                saved.userLoginBonuses.Select(b => b.loginBonusType).SequenceEqual(["beginner", "normal"]),
                "首次弹窗顺序与持久登录记录顺序分别处理");
            Check.That(saved.userPresents.Count == 2 && saved.userPresents.Sum(p => p.resourceQuantity) == 5 &&
                saved.userMaterials.Length == 0 && saved.userPresents.Select(p => p.presentId).Distinct().Count() == 2,
                "同资源的两份登录奖励独立入邮箱，不提前增加背包材料");
            Check.That(saved.userPresents.All(p => p.grantedAt == saved.userGamedata.lastLoginAt &&
                p.seq == long.MaxValue - p.grantedAt && p.expiredAt - p.grantedAt == 30L * 24 * 60 * 60 * 1000),
                "登录礼物保留发放时间、排序值和已核验期限");
            var ids = saved.userPresents.Select(p => p.presentId).ToArray();
            var repeat = DumpSerializer.Deserialize<UserLoginBonus[]>(operations.Execute(1, bonuses.ClaimInitial));
            saved = store.Read(1)!.Data;
            Check.That(repeat.Length == 0 && saved.userPresents.Select(p => p.presentId).SequenceEqual(ids) &&
                saved.userHonorMissions.All(m => m.progress == 1), "重复刷新不重复入邮箱或累加首日进度");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
