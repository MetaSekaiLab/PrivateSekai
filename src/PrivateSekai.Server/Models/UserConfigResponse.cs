extern alias game;

using System;
using game::Sekai;
using MessagePack;
using MessagePack.Formatters;

namespace PrivateSekai.Models;

[MessagePackFormatter(typeof(UserConfigResponseFormatter))]
public sealed class UserConfigResponse
{
    public required SuiteUser UpdatedResources { get; init; }
    public required UserConfig Config { get; init; }
}

public sealed class UserConfigResponseFormatter : IMessagePackFormatter<UserConfigResponse?>
{
    public void Serialize(ref MessagePackWriter writer, UserConfigResponse? value, MessagePackSerializerOptions options)
    {
        if (value == null) { writer.WriteNil(); return; }
        writer.WriteMapHeader(2);
        writer.Write("updatedResources");
        MessagePackSerializer.Serialize(ref writer, value.UpdatedResources, options);
        writer.Write("userConfig");
        MessagePackSerializer.Serialize(ref writer, value.Config, options);
    }

    public UserConfigResponse? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
        throw new NotSupportedException("Response-only formatter.");
}
