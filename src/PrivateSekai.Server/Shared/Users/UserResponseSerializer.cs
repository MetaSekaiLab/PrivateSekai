extern alias game;

using System;
using System.Linq;
using game::Sekai;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using PrivateSekai.Protocol;

namespace PrivateSekai.Shared.Users;

internal static class UserResponseSerializer
{
    private static readonly MessagePackSerializerOptions Options = DumpSerializer.Options.WithResolver(
        CompositeResolver.Create(
            new IMessagePackFormatter[]
            {
                new WithoutUserIdFormatter<UserCharacterMissionV2>(_ => true),
                new WithoutUserIdFormatter<UserMissionStatus>(s => s.missionType == "beginner_mission_v2")
            },
            new[] { DumpSerializer.Options.Resolver }));

    public static byte[] Serialize(object? value) => value == null ? [0xc0] :
        MessagePackSerializer.Serialize(value.GetType(), value, Options);

    private sealed class WithoutUserIdFormatter<T>(Func<T, bool> omit) : IMessagePackFormatter<T?> where T : class
    {
        private static readonly DumpMember[] Members = DumpContract.For(typeof(T)).Members
            .Where(m => (string)m.Key != "userId").ToArray();

        public void Serialize(ref MessagePackWriter writer, T? value, MessagePackSerializerOptions options)
        {
            if (value == null) { writer.WriteNil(); return; }
            if (!omit(value))
            {
                DumpSerializer.Options.Resolver.GetFormatterWithVerify<T>().Serialize(ref writer, value, options);
                return;
            }
            var members = Members.Where(m => m.Get(value) != null).ToArray();
            writer.WriteMapHeader(members.Length);
            foreach (var member in members)
            {
                writer.Write((string)member.Key);
                MessagePackSerializer.Serialize(member.Type, ref writer, member.Get(value), options);
            }
        }

        public T? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<T>().Deserialize(ref reader, DumpSerializer.Options);
    }
}
