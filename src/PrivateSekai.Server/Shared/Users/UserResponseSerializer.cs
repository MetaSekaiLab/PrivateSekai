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
                new ChallengeStartFormatter(),
                new EventExchangeFormatter(),
                new FieldFilterFormatter<UserEventExchange>((s, key) =>
                    key == "exchangeRemaining" && s.exchangeStatus == "exchangeable" && s.exchangeRemaining == 0),
                new FieldFilterFormatter<UserMaterialExchange>((s, key) =>
                    (key == "lastExchangedAt" && s.lastExchangedAt == 0) || (key == "refreshedAt" && s.refreshedAt == 0) ||
                    (key == "exchangeRemaining" && s.exchangeStatus == "exchangeable" && s.exchangeRemaining == 0 && s.refreshedAt == 0)),
                new FieldFilterFormatter<UserCharacterMissionV2>((_, key) => key == "userId"),
                new FieldFilterFormatter<UserReleaseCondition>((_, key) => key == "userId"),
                new FieldFilterFormatter<UserPresentData>((s, key) => (key == "grantedAt" && s.grantedAt == 0) ||
                    (s.grantedAt > 0 && ((key == "resourceId" && s.resourceId == 0) || (key == "resourceLevel" && s.resourceLevel == 0) ||
                        (key == "expiredAt" && s.expiredAt == 0)))),
                new FieldFilterFormatter<UserGamedata>((s, key) => key == "lastLoginAt" && s.lastLoginAt == 0),
                new FieldFilterFormatter<UserHonorMission>((_, key) => key == "userId"),
                new FieldFilterFormatter<UserHomeRefreshResponse>((s, key) => key == "shouldReflectWebPayment" && !s.shouldReflectWebPayment),
                new FieldFilterFormatter<UserMissionStatus>((s, key) => key == "userId" && s.missionType == "beginner_mission_v2"),
                new FieldFilterFormatter<UserChallengeLivePlayStatus>((s, key) => key == "playEndAt" && s.liveStatus == "start")
            },
            new[] { DumpSerializer.Options.Resolver }));

    public static byte[] Serialize(object? value) => value == null ? [0xc0] :
        MessagePackSerializer.Serialize(value.GetType(), value, Options);

    internal sealed class EventExchangeFormatter : IMessagePackFormatter<UserEventExchangeResponse?>
    {
        public void Serialize(ref MessagePackWriter writer, UserEventExchangeResponse? value, MessagePackSerializerOptions options)
        {
            var scoped = options.WithResolver(CompositeResolver.Create(
                new IMessagePackFormatter[] { new FieldFilterFormatter<UserResource>((r, key) =>
                    (key == "resourceLevel" && r.resourceLevel == 0) ||
                    (key == "resourceId" && r.resourceType == "coin" && r.resourceId == 0)) },
                new[] { options.Resolver }));
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<UserEventExchangeResponse>()
                .Serialize(ref writer, value!, scoped);
        }

        public UserEventExchangeResponse? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<UserEventExchangeResponse>().Deserialize(ref reader, DumpSerializer.Options);
    }

    internal sealed class ChallengeStartFormatter : IMessagePackFormatter<UserChallengeLiveStartResponse?>
    {
        public void Serialize(ref MessagePackWriter writer, UserChallengeLiveStartResponse? value, MessagePackSerializerOptions options)
        {
            var scoped = options.WithResolver(CompositeResolver.Create(
                new IMessagePackFormatter[] { new FieldFilterFormatter<IngameLotterySkill>((_, key) => key == "ingameCutinCharacterId") },
                new[] { options.Resolver }));
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<UserChallengeLiveStartResponse>()
                .Serialize(ref writer, value!, scoped);
        }

        public UserChallengeLiveStartResponse? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<UserChallengeLiveStartResponse>()
                .Deserialize(ref reader, DumpSerializer.Options);
    }

    private sealed class FieldFilterFormatter<T>(Func<T, string, bool> omit) : IMessagePackFormatter<T?> where T : class
    {
        private static readonly DumpMember[] Members = DumpContract.For(typeof(T)).Members.ToArray();

        public void Serialize(ref MessagePackWriter writer, T? value, MessagePackSerializerOptions options)
        {
            if (value == null) { writer.WriteNil(); return; }
            var members = Members.Where(m => !omit(value, (string)m.Key) && m.Get(value) != null).ToArray();
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
