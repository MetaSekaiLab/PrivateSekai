extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Models;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class UserOperationChecks
{
    public static void Run()
    {
        FailureDoesNotCommit();
        PrivateStateIsIsolated();
        ReferencesDoNotEscape();
        SameUserUpdatesAreSerialized();
        DifferentUsersRemainIndependent();
        NestedOperationsAreRejected();
        RefreshIsStable();
        MissionResponseFields();
        RegistrationIsExplicit();
        RegistrationDoesNotOverwriteRestoredUser();
        Console.WriteLine("用户操作：回滚、引用隔离、并发、刷新及注册检查通过。");
    }

    private static void MissionResponseFields()
    {
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userCharacterMissions = [new() { userId = 1, characterId = 1, characterMissionType = "read_card_episode_first", progress = 1,
            achievedMissions = [new() { userId = 1, missionId = 1006 }] }];
        state.Data.userMissionStatuses = [new() { userId = 1, missionType = "beginner_mission_v2", missionId = 7 },
            new() { userId = 1, missionType = "live_mission", missionId = 1 }];
        store.Save(1, state);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var bytes = operation.Execute(1, () => new SuiteUserCommonResponse { updatedResources = user.BuildSuite() });
        using var json = JsonDocument.Parse(MessagePackSerializer.ConvertToJson(bytes));
        var suite = json.RootElement.GetProperty("updatedResources");
        var progress = suite.GetProperty("userCharacterMissionV2s")[0];
        Check.That(!progress.TryGetProperty("userId", out _) &&
            progress.GetProperty("achievedMissions")[0].GetProperty("userId").GetInt64() == 1,
            "响应省略角色任务进度用户 ID，但保留达成状态用户 ID");
        var statuses = suite.GetProperty("userMissionStatuses");
        Check.That(!statuses[0].TryGetProperty("userId", out _) && !statuses[1].TryGetProperty("userId", out _),
            "省略已核验的新手任务与 Live 任务状态用户 ID");
        Check.That(store.Read(1)!.Data.userCharacterMissions[0].userId == 1 &&
            DumpSerializer.Deserialize<SuiteUser>(DumpSerializer.Serialize(state.Data)).userMissionStatuses[0].userId == 1,
            "响应字段省略不修改存储或原始 dump 往返契约");
    }

    private static void FailureDoesNotCommit()
    {
        var memory = new MemoryUserStore();
        memory.Save(1, TestUsers.Create(1));
        var failing = new FailingStore(memory);
        using var provider = TestUsers.Provider(failing);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();

        Check.Throws<InvalidOperationException>(() => operation.Execute(1, () =>
        {
            user.Data.userGamedata.coin = 7;
            throw new InvalidOperationException("fixture business failure");
        }), "业务异常向外返回");
        Check.That(memory.Read(1)!.Data.userGamedata.coin == 100, "业务异常不提交");
        Check.Throws<InvalidOperationException>(() => _ = user.Data, "异常后关闭用户上下文");

        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            user.Data.userGamedata.coin = 8;
            return new BrokenResponse();
        }), "响应序列化异常向外返回");
        Check.That(memory.Read(1)!.Data.userGamedata.coin == 100, "响应序列化失败不提交");

        failing.FailWrites = true;
        Check.Throws<InvalidOperationException>(() => operation.Execute(1, () =>
        {
            user.Data.userGamedata.coin = 9;
            return null;
        }), "存储失败向外返回");
        Check.That(memory.Read(1)!.Data.userGamedata.coin == 100, "存储失败保留原快照");
        var query = operation.Query(1, () =>
        {
            user.Data.userGamedata.coin = 12;
            user.MarkChanged(nameof(SuiteUser.userGamedata));
            return user.BuildRefresh();
        });
        Check.That(DumpSerializer.Deserialize<SuiteUser>(query).userGamedata.coin == 12 &&
            memory.Read(1)!.Data.userGamedata.coin == 100, "只读查询可映射响应但不调用存储提交");
        failing.FailWrites = false;
        operation.Execute(1, () =>
        {
            user.Data.userGamedata.coin++;
            return null;
        });
        Check.That(memory.Read(1)!.Data.userGamedata.coin == 101, "失败后同一作用域可以重新操作");
    }

    private static void PrivateStateIsIsolated()
    {
        var source = TestUsers.Create(1);
        source.Private.PresentHistories.Add(new UserPresentHistoryData { presentId = "fixture-present", resourceQuantity = 3 });
        source.Private.UserLiveSessions.Add("fixture-live", new UserLiveSessionData { UserLiveId = "fixture-live", BoostCount = 2 });
        var store = new MemoryUserStore();
        store.Save(1, source);
        source.Private.PresentHistories[0].resourceQuantity = 99;
        source.Private.UserLiveSessions["fixture-live"].BoostCount = 99;
        var read = store.Read(1)!;
        Check.That(read.Private.PresentHistories[0].resourceQuantity == 3, "存储隔离礼物历史元素");
        Check.That(read.Private.UserLiveSessions["fixture-live"].BoostCount == 2, "存储隔离 Live 会话对象");
        read.Private.PresentHistories[0].resourceQuantity = 88;
        read.Private.UserLiveSessions["fixture-live"].BoostCount = 88;
        Check.That(store.Read(1)!.Private.PresentHistories[0].resourceQuantity == 3, "读取礼物历史也深隔离");
        Check.That(store.Read(1)!.Private.UserLiveSessions["fixture-live"].BoostCount == 2, "读取 Live 会话也深隔离");

        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        Check.Throws<InvalidOperationException>(() => operation.Execute(1, () =>
        {
            user.Private.PresentHistories[0].resourceQuantity = 1;
            user.Private.UserLiveSessions["fixture-live"].BoostCount = 1;
            throw new InvalidOperationException("fixture private-state failure");
        }), "私有状态操作失败");
        Check.That(store.Read(1)!.Private.PresentHistories[0].resourceQuantity == 3, "失败不污染历史元素");
        Check.That(store.Read(1)!.Private.UserLiveSessions["fixture-live"].BoostCount == 2, "失败不污染 Live 会话");
    }

    private static void ReferencesDoNotEscape()
    {
        var store = new MemoryUserStore();
        store.Save(1, TestUsers.Create(1));
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var requestProfile = new UserProfile { word = "saved" };
        SuiteUser? response = null;
        var bytes = operation.Execute(1, () =>
        {
            user.Data.userProfile = requestProfile;
            user.MarkChanged(nameof(SuiteUser.userProfile));
            return response = user.BuildRefresh();
        });
        requestProfile.word = "request changed";
        response!.userProfile.word = "response changed";
        Check.That(store.Read(1)!.Data.userProfile.word == "saved", "提交后请求和响应引用不污染存储");
        Check.That(DumpSerializer.Deserialize<SuiteUser>(bytes).userProfile.word == "saved", "响应在提交前已序列化");
    }

    private static void SameUserUpdatesAreSerialized()
    {
        var store = new MemoryUserStore();
        store.Save(1, TestUsers.Create(1, 0));
        using var provider = TestUsers.Provider(store);
        Parallel.For(0, 64, _ =>
        {
            using var scope = provider.CreateScope();
            var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            operation.Execute(1, () =>
            {
                var before = user.Data.userGamedata.coin;
                Thread.Yield();
                user.Data.userGamedata.coin = before + 1;
                return null;
            });
        });
        Check.That(store.Read(1)!.Data.userGamedata.coin == 64, "同用户多作用域并发无丢更新");
    }

    private static void DifferentUsersRemainIndependent()
    {
        var store = new MemoryUserStore();
        store.Save(1, TestUsers.Create(1));
        store.Save(2, TestUsers.Create(2));
        using var provider = TestUsers.Provider(store);
        using var rendezvous = new Barrier(2);
        var workers = new[] { 1L, 2L }.Select(id => Task.Factory.StartNew(() =>
        {
            using var scope = provider.CreateScope();
            var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            operation.Execute(id, () =>
            {
                if (!rendezvous.SignalAndWait(TimeSpan.FromSeconds(5)))
                    throw new InvalidOperationException("不同用户的操作被串行阻塞。");
                user.Data.userGamedata.coin = (int)id;
                return null;
            });
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        Task.WaitAll(workers);
        Check.That(store.Read(1)!.Data.userGamedata.coin == 1 && store.Read(2)!.Data.userGamedata.coin == 2,
            "不同用户可同时执行且状态独立");
    }

    private static void NestedOperationsAreRejected()
    {
        var store = new MemoryUserStore();
        store.Save(1, TestUsers.Create(1));
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        operation.Execute(1, () =>
        {
            Check.Throws<InvalidOperationException>(() => operation.Execute(1, () => null), "同作用域拒绝嵌套操作");
            using var nestedScope = provider.CreateScope();
            var nested = nestedScope.ServiceProvider.GetRequiredService<UserOperation>();
            Check.Throws<InvalidOperationException>(() => nested.Execute(1, () => null), "同用户跨作用域拒绝嵌套操作");
            return null;
        });
    }

    private static void RefreshIsStable()
    {
        var state = TestUsers.Create(1);
        state.Data.userCards = [new UserCard { cardId = 1, level = 2 }];
        state.Data.userMaterials = [new UserMaterial { materialId = 1, quantity = 3 }];
        var store = new MemoryUserStore();
        store.Save(1, state);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        operation.Execute(1, () =>
        {
            user.MarkChanged(nameof(SuiteUser.userCards));
            user.MarkChanged(nameof(SuiteUser.userMaterials));
            var first = user.BuildRefresh();
            var second = user.BuildRefresh();
            Check.That(first.userCards[0].level == 2 && first.userMaterials[0].quantity == 3, "刷新合并多个模块变化");
            Check.That(DumpSerializer.Serialize(first).SequenceEqual(DumpSerializer.Serialize(second)), "重复读取刷新不消耗变化");
            var excluded = user.BuildRefresh(["userCards", "userBeginnerMissionBehavior"]);
            Check.That(excluded.userCards == null && excluded.userMaterials[0].quantity == 3, "刷新支持排除字段并保留其他模块");
            var afterExcluded = user.BuildRefresh();
            Check.That(afterExcluded.userCards[0].cardId == 1, "排除字段不删除变化记录");
            Check.That(first.now == user.Now && second.now == user.Now, "一次操作使用固定时间");
            return second;
        });
    }

    private static void RegistrationIsExplicit()
    {
        var store = new MemoryUserStore();
        using var provider = TestUsers.Provider(store);
        using (var scope = provider.CreateScope())
        {
            var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
            Check.That(operation.Read(404) == null, "查询未知用户返回缺失");
            Check.Throws<KeyNotFoundException>(() => operation.Execute(404, () => null), "未知用户不能隐式创建");
            Check.That(operation.GetUserIds().Length == 0, "未知用户查询不注册");
            Check.Throws<InvalidOperationException>(() => operation.Create((_, _) => throw new InvalidOperationException("fixture initialization failure"), () => null), "注册初始化失败");
            Check.Throws<InvalidOperationException>(() => operation.Create((id, _) => TestUsers.Create(id), () => throw new InvalidOperationException("fixture registration failure")), "注册业务失败");
            Check.That(store.GetUserIds().Length == 0, "注册失败不留下用户");
        }

        Parallel.For(0, 16, _ =>
        {
            using var scope = provider.CreateScope();
            var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            operation.Create((id, _) => TestUsers.Create(id), () => user.BuildSuite());
        });
        var ids = store.GetUserIds();
        Check.That(ids.Length == 16 && ids.Distinct().Count() == 16, "并发注册生成唯一 ID");
        Check.That(ids.All(id => store.Read(id)!.Data.userRegistration.userId == id), "注册 ID 与用户状态一致");
    }

    private static void RegistrationDoesNotOverwriteRestoredUser()
    {
        var store = new ReservationRaceStore(new MemoryUserStore());
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var createCalls = 0;
        var actionCalls = 0;
        var bytes = operation.Create((id, _) =>
        {
            createCalls++;
            return TestUsers.Create(id, 20);
        }, () =>
        {
            actionCalls++;
            user.Data.userGamedata.coin++;
            return user.BuildSuite();
        });

        var registered = DumpSerializer.Deserialize<SuiteUser>(bytes);
        Check.That(store.ReserveCalls == 2, "注册发现预留 ID 已被恢复时重新分配");
        Check.That(registered.userRegistration.userId == 2, "注册使用恢复账号之后的新 ID");
        Check.That(createCalls == 1 && actionCalls == 1, "ID 冲突不会重复执行用户初始化或业务");
        Check.That(store.Read(1)!.Data.userGamedata.coin == 77 && store.Read(1)!.Data.userGamedata.name == "restored",
            "注册不会覆盖抢先恢复的账号状态");
        Check.That(store.Read(2)!.Data.userGamedata.coin == 21, "新账号只提交一次业务结果");

        var restoreCreates = 0;
        var restoreActions = 0;
        var restoredBytes = operation.Restore(1, (id, _) =>
        {
            restoreCreates++;
            return TestUsers.Create(id);
        }, () =>
        {
            restoreActions++;
            return user.BuildSuite();
        });
        var restored = DumpSerializer.Deserialize<SuiteUser>(restoredBytes);
        Check.That(restoreCreates == 0 && restoreActions == 1, "Restore 已有账号时不重新初始化");
        Check.That(restored.userRegistration.userId == 1 && restored.userGamedata.coin == 77 &&
            store.Read(2)!.Data.userGamedata.coin == 21, "Restore 保留已有账号状态且不影响新账号");
    }

    private sealed class ReservationRaceStore(IUserStore inner) : IUserStore
    {
        public int ReserveCalls { get; private set; }

        public long ReserveId()
        {
            var id = inner.ReserveId();
            if (++ReserveCalls == 1)
            {
                // 模拟账号恢复在注册拿到用户锁之前完成。
                var restored = TestUsers.Create(id, 77);
                restored.Data.userGamedata.name = "restored";
                inner.Save(id, restored);
            }
            return id;
        }

        public long[] GetUserIds() => inner.GetUserIds();
        public UserState? Read(long userId) => inner.Read(userId);
        public void Save(long userId, UserState state) => inner.Save(userId, state);
    }

    private sealed class FailingStore(IUserStore inner) : IUserStore
    {
        public bool FailWrites { get; set; }
        public long ReserveId() => inner.ReserveId();
        public long[] GetUserIds() => inner.GetUserIds();
        public UserState? Read(long userId) => inner.Read(userId);
        public void Save(long userId, UserState state)
        {
            if (FailWrites)
                throw new InvalidOperationException("fixture store failure");
            inner.Save(userId, state);
        }
    }
}

[MessagePackObject]
public sealed class BrokenResponse
{
    [Key("value")]
    public int Value
    {
        get => throw new InvalidOperationException("fixture serialization failure");
        set { }
    }
}
