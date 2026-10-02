using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PrivateSekai.Protocol;

// JSON 仅用于本地模板和原始 master 文件；网络协议直接使用 MessagePack。
public static class DumpJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (!DumpContract.Supports(info.Type) || info.Kind != JsonTypeInfoKind.Object) return;
            info.Properties.Clear();
            if (!info.Type.IsAbstract) info.CreateObject = () => RuntimeHelpers.GetUninitializedObject(info.Type);
            foreach (var member in DumpContract.For(info.Type).Members)
            {
                var name = member.Key is string key ? key : member.Member.Name;
                var property = info.CreateJsonPropertyInfo(member.Type, name);
                property.Get = member.Get;
                property.Set = member.Set;
                info.Properties.Add(property);
                // master JSON 中少数字段使用 CLR 名称，网络 Key 仍以 dump 元数据为准。
                if (name != member.Member.Name)
                {
                    var alias = info.CreateJsonPropertyInfo(member.Type, member.Member.Name);
                    alias.Get = member.Get;
                    alias.Set = member.Set;
                    alias.ShouldSerialize = (_, _) => false;
                    info.Properties.Add(alias);
                }
            }
        });
        return new JsonSerializerOptions { IncludeFields = true, TypeInfoResolver = resolver };
    }
}
