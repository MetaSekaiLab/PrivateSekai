extern alias game;

using System;
using System.Collections;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using game::Sekai;
using PrivateSekai.Models;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Storage;

internal sealed record UserBlock(string Name, Type Type, Func<UserState, object?> Get, Action<UserState, object?> Set);

internal static class UserBlocks
{
    // 未识别字段拒绝读取，避免旧程序保存时静默丢失新版本数据。
    internal static readonly JsonSerializerOptions Json = new(DumpJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static readonly UserBlock[] All = DumpContract.For(typeof(SuiteUser)).Members
        .Where(m => (string)m.Key is not ("now" or "refreshableTypes"))
        .Select(m => new UserBlock("data/" + m.Key, m.Type, s => m.Get(s.Data), (s, v) => m.Set(s.Data, v)))
        .Concat(typeof(NotSuiteData).GetProperties().Select(p => new UserBlock("private/" + p.Name,
            p.PropertyType, s => p.GetValue(s.Private), (s, v) => p.SetValue(s.Private, v))))
        .ToArray();

    // 比较隔离副本与已提交快照；只对变化块执行 JSON 编解码。
    internal static bool Equal(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.GetType() != right.GetType()) return false;
        var type = left.GetType();
        if (left is byte[] leftBytes && right is byte[] rightBytes)
            return leftBytes.AsSpan().SequenceEqual(rightBytes);
        if (type.IsValueType || left is string) return left.Equals(right);
        if (left is IDictionary a && right is IDictionary b)
        {
            if (a.Count != b.Count) return false;
            foreach (DictionaryEntry item in a)
                if (!b.Contains(item.Key) || !Equal(item.Value, b[item.Key])) return false;
            return true;
        }
        if (left is IList x && right is IList y)
        {
            if (x.Count != y.Count) return false;
            for (var i = 0; i < x.Count; i++)
                if (!Equal(x[i], y[i])) return false;
            return true;
        }
        var info = Json.GetTypeInfo(type);
        if (info.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)
            throw new NotSupportedException("Unsupported user block value.");
        return info.Properties.Where(p => p.Get != null)
            .All(p => Equal(p.Get!(left), p.Get!(right)));
    }
}
