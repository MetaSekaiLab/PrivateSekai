using System;
using System.Collections.Generic;
using System.Linq;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Storage;

public sealed class MemoryUserStore : IUserStore
{
    private readonly Dictionary<long, UserState> _users = new();
    private readonly object _gate = new();
    private long _lastId;

    public long ReserveId()
    {
        lock (_gate) return ++_lastId;
    }

    public long[] GetUserIds()
    {
        lock (_gate) return _users.Keys.Order().ToArray();
    }

    public UserState? Read(long userId)
    {
        UserState? state;
        lock (_gate) _users.TryGetValue(userId, out state);
        return state?.DeepClone();
    }

    public void Save(long userId, UserState state)
    {
        if (state.Data.userRegistration?.userId != userId)
            throw new InvalidOperationException("User identity does not match the store key.");
        var snapshot = state.DeepClone();
        lock (_gate)
        {
            _users[userId] = snapshot;
            _lastId = Math.Max(_lastId, userId);
        }
    }
}
