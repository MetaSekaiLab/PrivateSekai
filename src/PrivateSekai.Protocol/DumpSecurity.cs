using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MessagePack;

namespace PrivateSekai.Protocol;

internal sealed class DumpSecurity : MessagePackSecurity
{
    private readonly ConcurrentDictionary<Type, object> comparers = new();

    public DumpSecurity() : base(UntrustedData) { }
    private DumpSecurity(DumpSecurity source) : base(source) { }
    protected override MessagePackSecurity Clone() => new DumpSecurity(this);

    protected override IEqualityComparer<T> GetHashCollisionResistantEqualityComparer<T>()
    {
        if (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            return (IEqualityComparer<T>)comparers.GetOrAdd(typeof(T), type => Activator.CreateInstance(
                typeof(PairComparer<,>).MakeGenericType(type.GenericTypeArguments), this)!);
        return base.GetHashCollisionResistantEqualityComparer<T>();
    }

    private sealed class PairComparer<TKey, TValue>(MessagePackSecurity security) : IEqualityComparer<KeyValuePair<TKey, TValue>>
    {
        private readonly IEqualityComparer<TKey> keys = security.GetEqualityComparer<TKey>();
        private readonly IEqualityComparer<TValue> values = security.GetEqualityComparer<TValue>();
        public bool Equals(KeyValuePair<TKey, TValue> x, KeyValuePair<TKey, TValue> y) =>
            keys.Equals(x.Key, y.Key) && values.Equals(x.Value, y.Value);
        public int GetHashCode(KeyValuePair<TKey, TValue> pair)
        {
            var hash = new HashCode();
            hash.Add(pair.Key, keys);
            hash.Add(pair.Value, values);
            return hash.ToHashCode();
        }
    }
}
