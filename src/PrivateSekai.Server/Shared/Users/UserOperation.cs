using System;
using System.Collections.Generic;
using System.Threading;

namespace PrivateSekai.Shared.Users;

public sealed class UserOperation(IUserStore store, UserLocks locks, UserSession session, TimeProvider clock,
    IEnumerable<IUserRefreshHandler>? refreshHandlers = null)
{
    public long[] GetUserIds() => store.GetUserIds();
    public UserState? Read(long userId) => store.Read(userId);

    public byte[] Execute(long userId, Func<object?> action) =>
        Run(userId, action, null, true);

    public byte[] Query(long userId, Func<object?> action) =>
        Run(userId, action, null, false);

    public byte[] ExecutePair(long userId, long otherUserId, Func<UserState, object?> action)
    {
        EnsureIdle();
        if (userId == otherUserId)
            throw new ArgumentException("A paired operation requires two different users.");
        var firstGate = GetGate(Math.Min(userId, otherUserId));
        var secondGate = GetGate(Math.Max(userId, otherUserId));
        lock (firstGate)
        lock (secondGate)
        {
            var state = store.Read(userId) ?? throw new KeyNotFoundException("User not found.");
            var other = store.Read(otherUserId) ?? throw new KeyNotFoundException("Other user not found.");
            session.Begin(state, clock.GetUtcNow().ToUnixTimeMilliseconds(), refreshHandlers);
            try
            {
                var response = UserResponseSerializer.Serialize(action(other));
                store.SaveMany(new Dictionary<long, UserState> { [userId] = state, [otherUserId] = other });
                return response;
            }
            finally
            {
                session.End();
            }
        }
    }

    // 仅由已验证凭证的账号入口恢复内存中缺失的用户。
    public byte[] Restore(long userId, Func<long, long, UserState> create, Func<object?> action) =>
        Run(userId, action, now => store.Read(userId) ?? create(userId, now), true);

    public byte[] Create(Func<long, long, UserState> create, Func<object?> action)
    {
        EnsureIdle();
        while (true)
        {
            var userId = store.ReserveId();
            lock (GetGate(userId))
            {
                // 旧账号可能在分配 ID 后先完成恢复。
                if (store.Read(userId) != null)
                    continue;
                return RunLocked(userId, action, now => create(userId, now), true);
            }
        }
    }

    private byte[] Run(long userId, Func<object?> action, Func<long, UserState>? create, bool commit)
    {
        EnsureIdle();
        lock (GetGate(userId))
            return RunLocked(userId, action, create, commit);
    }

    private byte[] RunLocked(long userId, Func<object?> action, Func<long, UserState>? create, bool commit)
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var state = create?.Invoke(now) ?? store.Read(userId)
            ?? throw new KeyNotFoundException("User not found.");
        session.Begin(state, now, refreshHandlers);
        try
        {
            var response = UserResponseSerializer.Serialize(action());
            if (commit)
                store.Save(userId, state);
            return response;
        }
        finally
        {
            session.End();
        }
    }

    private object GetGate(long userId)
    {
        var gate = locks.For(userId);
        if (Monitor.IsEntered(gate))
            throw new InvalidOperationException("Nested user operations are not supported.");
        return gate;
    }

    private void EnsureIdle()
    {
        if (session.IsActive)
            throw new InvalidOperationException("Nested user operations are not supported.");
    }
}
