extern alias game;

using System;
using System.Linq;
using game::Sekai;
using MessagePack;
using MessagePack.Formatters;
using PrivateSekai.Protocol;

namespace PrivateSekai.Models;

[MessagePackFormatter(typeof(UnitStoryResponseFormatter))]
public sealed record UnitStoryResponse(UserStoryResponse Response);

public sealed class UnitStoryResponseFormatter : IMessagePackFormatter<UnitStoryResponse?>
{
    public void Serialize(ref MessagePackWriter writer, UnitStoryResponse? value, MessagePackSerializerOptions options)
    {
        if (value == null) { writer.WriteNil(); return; }
        writer.WriteMapHeader(2);
        writer.Write("updatedResources");
        options.Resolver.GetFormatterWithVerify<SuiteUser>().Serialize(ref writer, value.Response.updatedResources, options);
        writer.Write("obtainedResources");
        writer.WriteArrayHeader(value.Response.obtainedResources.Length);
        foreach (var resource in value.Response.obtainedResources)
        {
            if (resource.resourceType is not ("jewel" or "material" or "card") || resource.resourceLevel != 0)
            {
                options.Resolver.GetFormatterWithVerify<UserResource>().Serialize(ref writer, resource, options);
                continue;
            }
            // 主线奖励的已确认字段省略规则，不影响其他资源响应。
            var members = DumpContract.For(typeof(UserResource)).Members.Where(m =>
                (string)m.Key != "resourceLevel" &&
                !((string)m.Key == "resourceId" && resource.resourceType == "jewel" && resource.resourceId == 0) &&
                m.Get(resource) != null).ToArray();
            writer.WriteMapHeader(members.Length);
            foreach (var member in members)
            {
                writer.Write((string)member.Key);
                MessagePackSerializer.Serialize(member.Type, ref writer, member.Get(resource), options);
            }
        }
    }

    public UnitStoryResponse? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
        throw new NotSupportedException("仅用于服务端主线响应编码。");
}
