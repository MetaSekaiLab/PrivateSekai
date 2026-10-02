using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace PrivateSekai.Protocol;

public static class DumpSerializer
{
    public static MessagePackSerializerOptions Options { get; } = MessagePackSerializerOptions.Standard
        .WithResolver(CompositeResolver.Create(DumpResolver.Instance, StandardResolver.Instance))
        .WithSecurity(new DumpSecurity());

    public static byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value, Options);
    public static byte[] SerializeObject(object? value) => value is null ? [0xc0] : MessagePackSerializer.Serialize(value.GetType(), value, Options);
    public static T Deserialize<T>(ReadOnlyMemory<byte> bytes) => MessagePackSerializer.Deserialize<T>(bytes, Options);
}

internal sealed class DumpResolver : IFormatterResolver
{
    public static readonly DumpResolver Instance = new();
    public IMessagePackFormatter<T>? GetFormatter<T>() => Cache<T>.Formatter;
    private static class Cache<T>
    {
        public static readonly IMessagePackFormatter<T>? Formatter = Create();
        private static IMessagePackFormatter<T>? Create()
        {
            if (!DumpContract.Supports(typeof(T))) return null;
            var contract = DumpContract.For(typeof(T));
            return contract.Unions.Count == 0 ? new ModelFormatter<T>(contract) : new UnionFormatter<T>(contract);
        }
    }
}

internal sealed class ModelFormatter<T> : IMessagePackFormatter<T>
{
    private readonly MemberCodec<T>[] members;
    private readonly Dictionary<string, MemberCodec<T>> byName;
    private readonly Dictionary<int, MemberCodec<T>> byIndex;
    private readonly bool isArray;
    private readonly int arrayLength;

    public ModelFormatter(DumpContract contract)
    {
        members = contract.Members.Select(m => (MemberCodec<T>)Activator.CreateInstance(
            typeof(MemberCodec<,>).MakeGenericType(typeof(T), m.Type), m)!).ToArray();
        isArray = contract.IsArray;
        byName = isArray ? [] : members.ToDictionary(m => (string)m.Key, StringComparer.Ordinal);
        byIndex = isArray ? members.ToDictionary(m => (int)m.Key) : [];
        arrayLength = isArray ? byIndex.Keys.Max() + 1 : 0;
    }

    public void Serialize(ref MessagePackWriter writer, T value, MessagePackSerializerOptions options)
    {
        if (value is null) { writer.WriteNil(); return; }
        if (isArray)
        {
            writer.WriteArrayHeader(arrayLength);
            for (var i = 0; i < arrayLength; i++)
            {
                if (byIndex.TryGetValue(i, out var member)) member.Write(ref writer, ref value, options);
                else writer.WriteNil();
            }
        }
        else
        {
            var count = 0;
            foreach (var member in members) if (member.HasValue(ref value)) count++;
            writer.WriteMapHeader(count);
            foreach (var member in members)
            {
                if (!member.HasValue(ref value)) continue;
                writer.Write((string)member.Key);
                member.Write(ref writer, ref value, options);
            }
        }
    }

    public T Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            if (typeof(T).IsValueType) throw new MessagePackSerializationException("值类型不能读取 nil。");
            return default!;
        }
        var value = typeof(T).IsValueType ? default! : (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        var count = isArray ? reader.ReadArrayHeader() : reader.ReadMapHeader();
        options.Security.DepthStep(ref reader);
        try
        {
            for (var i = 0; i < count; i++)
            {
                reader.CancellationToken.ThrowIfCancellationRequested();
                MemberCodec<T>? member;
                if (isArray) byIndex.TryGetValue(i, out member);
                else byName.TryGetValue(reader.ReadString() ?? throw new MessagePackSerializationException("map Key 不能为 nil。"), out member);
                if (member != null) member.Read(ref reader, ref value, options);
                else reader.Skip();
            }
        }
        finally { reader.Depth--; }
        return value;
    }
}

internal abstract class MemberCodec<T>(object key)
{
    public object Key { get; } = key;
    public abstract bool HasValue(ref T value);
    public abstract void Write(ref MessagePackWriter writer, ref T value, MessagePackSerializerOptions options);
    public abstract void Read(ref MessagePackReader reader, ref T value, MessagePackSerializerOptions options);
}

internal sealed class MemberCodec<T, TValue> : MemberCodec<T>
{
    private delegate TValue Getter(ref T instance);
    private delegate void Setter(ref T instance, TValue value);
    private readonly Getter get;
    private readonly Setter set;
    private static readonly bool Nullable = !typeof(TValue).IsValueType || System.Nullable.GetUnderlyingType(typeof(TValue)) != null;

    public MemberCodec(DumpMember member) : base(member.Key)
    {
        var instance = Expression.Parameter(typeof(T).MakeByRefType(), "instance");
        var value = Expression.Parameter(typeof(TValue), "value");
        var access = Expression.MakeMemberAccess(instance, member.Member);
        get = Expression.Lambda<Getter>(access, instance).Compile();
        set = Expression.Lambda<Setter>(Expression.Assign(access, value), instance, value).Compile();
    }

    public override bool HasValue(ref T value) => !Nullable || get(ref value) is not null;
    public override void Write(ref MessagePackWriter writer, ref T value, MessagePackSerializerOptions options) =>
        options.Resolver.GetFormatterWithVerify<TValue>().Serialize(ref writer, get(ref value), options);
    public override void Read(ref MessagePackReader reader, ref T value, MessagePackSerializerOptions options) =>
        set(ref value, options.Resolver.GetFormatterWithVerify<TValue>().Deserialize(ref reader, options));
}

internal sealed class UnionFormatter<T>(DumpContract contract) : IMessagePackFormatter<T>
{
    public void Serialize(ref MessagePackWriter writer, T value, MessagePackSerializerOptions options)
    {
        if (value is null) { writer.WriteNil(); return; }
        var pair = contract.Unions.SingleOrDefault(p => p.Value == value.GetType());
        if (pair.Value == null) throw new MessagePackSerializationException($"未登记的 Union 类型：{value.GetType()}");
        writer.WriteArrayHeader(2);
        writer.Write(pair.Key);
        MessagePackSerializer.Serialize(pair.Value, ref writer, value, options);
    }

    public T Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil()) return default!;
        if (reader.ReadArrayHeader() != 2) throw new MessagePackSerializationException("Union 数组长度必须为 2。");
        var key = reader.ReadInt32();
        options.Security.DepthStep(ref reader);
        try
        {
            if (contract.Unions.TryGetValue(key, out var type))
                return (T)MessagePackSerializer.Deserialize(type, ref reader, options)!;
            reader.Skip();
            return default!;
        }
        finally { reader.Depth--; }
    }
}
