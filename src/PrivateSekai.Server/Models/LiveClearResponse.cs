extern alias game;

using System;
using System.Linq;
using game::Sekai;
using MessagePack;
using MessagePack.Formatters;
using PrivateSekai.Protocol;

namespace PrivateSekai.Models;

[MessagePackFormatter(typeof(LiveClearResponseFormatter))]
public sealed class LiveClearResponse : UserLiveClearResponse
{
    public string ScoreRank { get; init; } = "";
}

public sealed class LiveClearResponseFormatter : IMessagePackFormatter<LiveClearResponse?>
{
    public void Serialize(ref MessagePackWriter writer, LiveClearResponse? value, MessagePackSerializerOptions options)
    {
        if (value == null) { writer.WriteNil(); return; }
        var members = DumpContract.For(typeof(UserLiveClearResponse)).Members.Where(m => m.Get(value) != null).ToArray();
        writer.WriteMapHeader(members.Length + 1);
        writer.Write("scoreRank");
        writer.Write(value.ScoreRank);
        foreach (var member in members)
        {
            writer.Write((string)member.Key);
            MessagePackSerializer.Serialize(member.Type, ref writer, member.Get(value), options);
        }
    }

    public LiveClearResponse? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
        throw new NotSupportedException("仅用于服务端演出结算响应编码。");
}
