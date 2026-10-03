extern alias game;

using System;
using game::Sekai;
using MessagePack;
using MessagePack.Formatters;

namespace PrivateSekai.Models;

[MessagePackFormatter(typeof(CardUpdateResponseFormatter))]
public sealed class CardUpdateResponse
{
    public required SuiteUser UpdatedResources { get; init; }
    public bool IncludeEmptyResources { get; init; }
}

public sealed class CardUpdateResponseFormatter : IMessagePackFormatter<CardUpdateResponse?>
{
    public void Serialize(ref MessagePackWriter writer, CardUpdateResponse? value, MessagePackSerializerOptions options)
    {
        if (value == null) { writer.WriteNil(); return; }
        writer.WriteMapHeader(value.IncludeEmptyResources ? 2 : 1);
        writer.Write("updatedResources");
        MessagePackSerializer.Serialize(ref writer, value.UpdatedResources, options);
        if (value.IncludeEmptyResources)
        {
            writer.Write("obtainedResources");
            writer.WriteArrayHeader(0);
        }
    }

    public CardUpdateResponse? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
        throw new NotSupportedException("Response-only formatter.");
}
