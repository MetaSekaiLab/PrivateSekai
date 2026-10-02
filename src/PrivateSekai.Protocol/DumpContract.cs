using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PrivateSekai.Protocol;

public sealed record DumpMember(MemberInfo Member, Type Type, object Key)
{
    public object? Get(object value) => Member is FieldInfo field ? field.GetValue(value) : ((PropertyInfo)Member).GetValue(value);
    public void Set(object value, object? memberValue)
    {
        if (Member is FieldInfo field) field.SetValue(value, memberValue);
        else ((PropertyInfo)Member).SetValue(value, memberValue);
    }
}

public sealed class DumpContract
{
    private static readonly ConcurrentDictionary<Type, DumpContract> Cache = new();
    public IReadOnlyList<DumpMember> Members { get; }
    public bool IsArray { get; }
    public IReadOnlyDictionary<int, Type> Unions { get; }

    public static DumpContract For(Type type) => Cache.GetOrAdd(type, t => new DumpContract(t));
    public static bool Supports(Type type) => Attribute(type, "MessagePack.MessagePackObjectAttribute") != null
        || type.FullName == "UnityEngine.Vector3";

    private DumpContract(Type type)
    {
        var members = new List<DumpMember>();
        if (type.FullName == "UnityEngine.Vector3")
        {
            // 客户端 MessagePack.Unity.Vector3Formatter 使用 [x, y, z] Float32 数组。
            foreach (var name in new[] { "x", "y", "z" })
                members.Add(new DumpMember(type.GetField(name)!, typeof(float), members.Count));
        }
        else
        {
            var objectAttribute = Attribute(type, "MessagePack.MessagePackObjectAttribute")
                ?? throw new NotSupportedException($"缺少 MessagePackObject：{type.FullName}");
            if (Attribute(type, "MessagePack.MessagePackFormatterAttribute") != null)
                throw new NotSupportedException($"不能执行 dump 自定义 formatter：{type.FullName}");
            var implicitKeys = objectAttribute.ConstructorArguments.FirstOrDefault().Value is true;
            foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var memberType = member switch { FieldInfo f => f.FieldType, PropertyInfo p => p.PropertyType, _ => null };
                if (memberType == null || Attribute(member, "MessagePack.IgnoreMemberAttribute") != null) continue;
                var key = Attribute(member, "MessagePack.KeyAttribute");
                var isPublic = member is FieldInfo { IsPublic: true } or PropertyInfo { GetMethod.IsPublic: true };
                if (key == null && !(implicitKeys && isPublic)) continue;
                if (Attribute(member, "MessagePack.MessagePackFormatterAttribute") != null)
                    throw new NotSupportedException($"不能执行 dump 成员 formatter：{type.FullName}.{member.Name}");
                members.Add(new DumpMember(member, memberType, key?.ConstructorArguments.Single().Value ?? member.Name));
            }
        }
        if (members.Any(m => m.Key is not (string or int)) || members.Select(m => m.Key).Distinct().Count() != members.Count
            || (members.Any(m => m.Key is int) && members.Any(m => m.Key is string)))
            throw new NotSupportedException($"无效或混合 Key：{type.FullName}");
        if (members.Any(m => m.Key is int i && (i < 0 || i > 65535)))
            throw new NotSupportedException($"整数 Key 超出支持范围：{type.FullName}");
        Members = members;
        IsArray = members.Any(m => m.Key is int);
        Unions = type.GetCustomAttributesData().Where(a => a.AttributeType.FullName == "MessagePack.UnionAttribute")
            .ToDictionary(a => (int)a.ConstructorArguments[0].Value!, a => (Type)a.ConstructorArguments[1].Value!);
    }

    private static CustomAttributeData? Attribute(MemberInfo member, string name) =>
        member.GetCustomAttributesData().SingleOrDefault(a => a.AttributeType.FullName == name);
}
