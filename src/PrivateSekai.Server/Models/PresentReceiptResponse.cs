extern alias game;

using System;
using System.Linq;
using game::Sekai;
using MessagePack;
using MessagePack.Formatters;
using PrivateSekai.Protocol;

namespace PrivateSekai.Models;

// 练习券领取响应中已确认的字段差异；其余字段继续使用 dump 模型。
[MessagePackFormatter(typeof(PresentReceiptResponseFormatter))]
public sealed record PresentReceiptResponse(UserPresentReceiveResponse Response, long ReceivedAt);

public sealed class PresentReceiptResponseFormatter : IMessagePackFormatter<PresentReceiptResponse?>
{
    public void Serialize(ref MessagePackWriter writer, PresentReceiptResponse? value, MessagePackSerializerOptions options)
    {
        if (value == null) { writer.WriteNil(); return; }
        writer.WriteMapHeader(2);
        writer.Write("updatedResources");
        options.Resolver.GetFormatterWithVerify<SuiteUser>().Serialize(ref writer, value.Response.updatedResources, options);
        writer.Write("receivedUserPresents");
        writer.WriteArrayHeader(value.Response.receivedUserPresents.Count);
        foreach (var present in value.Response.receivedUserPresents)
        {
            if (present.resourceType != "practice_ticket" || present.resourceLevel != 0)
            {
                options.Resolver.GetFormatterWithVerify<UserPresentData>().Serialize(ref writer, present, options);
                continue;
            }
            var members = DumpContract.For(typeof(UserPresentData)).Members
                .Where(m => (string)m.Key != "resourceLevel" && m.Get(present) != null).ToArray();
            writer.WriteMapHeader(members.Length + 1);
            foreach (var member in members)
            {
                writer.Write((string)member.Key);
                MessagePackSerializer.Serialize(member.Type, ref writer, member.Get(present), options);
            }
            writer.Write("receivedAt");
            writer.Write(value.ReceivedAt);
        }
    }

    public PresentReceiptResponse? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
        throw new NotSupportedException("仅用于服务端领取响应编码。");
}
