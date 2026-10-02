extern alias game;

using System;
using System.Linq;
using game::Sekai;
using MessagePack;
using MessagePack.Formatters;
using PrivateSekai.Protocol;

namespace PrivateSekai.Models;

[MessagePackFormatter(typeof(StoryRewardResponseFormatter))]
public sealed record StoryRewardResponse(UserStoryResponse Response, string StoryType);

public sealed class StoryRewardResponseFormatter : IMessagePackFormatter<StoryRewardResponse?>
{
    public void Serialize(ref MessagePackWriter writer, StoryRewardResponse? value, MessagePackSerializerOptions options)
    {
        if (value == null) { writer.WriteNil(); return; }
        writer.WriteMapHeader(2);
        writer.Write("updatedResources");
        options.Resolver.GetFormatterWithVerify<SuiteUser>().Serialize(ref writer, value.Response.updatedResources, options);
        writer.Write("obtainedResources");
        writer.WriteArrayHeader(value.Response.obtainedResources.Length);
        foreach (var resource in value.Response.obtainedResources)
        {
            if (!(resource.resourceType == "jewel" || value.StoryType == "unit_story" && resource.resourceType is "material" or "card") || resource.resourceLevel != 0)
            {
                options.Resolver.GetFormatterWithVerify<UserResource>().Serialize(ref writer, resource, options);
                continue;
            }
            // 仅应用已核验的剧情奖励字段省略规则。
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

    public StoryRewardResponse? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
        throw new NotSupportedException("仅用于服务端剧情响应编码。");
}
