using System.Collections.Concurrent;

namespace PrivateSekai.Shared.Users;

public sealed class UserLocks
{
    private readonly ConcurrentDictionary<long, object> _locks = new();

    internal object For(long userId) => _locks.GetOrAdd(userId, static _ => new object());
}
