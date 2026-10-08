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
                new MissionReceiveFormatter(),
                new CostumeShopFormatter(),
                new CharacterMissionFormatter(),
                new FriendProfileFormatter(),
                new FieldFilterFormatter<UserFriend>((s, key) => key == "approvedAt" && s.approvedAt == 0),
                new FieldFilterFormatter<UserLoginStatus>((s, key) =>
                    key == "loginStatusUpdatedAt" && s.loginStatus == "offline" && s.loginStatusUpdatedAt == 0),
                new FieldFilterFormatter<UserHonor>((s, key) => key == "userId" && s.userId == 0),
                new FieldFilterFormatter<UserProfile>((s, key) => (key == "userId" && s.userId == 0) ||
                    (key == "profileImageId" && s.profileImageType == "leader" && s.profileImageId == 0)),
                new FieldFilterFormatter<UserCharacter>((_, key) => key == "userId"),
                new FieldFilterFormatter<UserStamp>((_, key) => key == "userId"),
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
                new FieldFilterFormatter<UserMissionStatus>((s, key) => key == "userId" &&
                    s.missionType is "beginner_mission_v2" or "live_mission" or "honor_mission"),
                new FieldFilterFormatter<UserLiveMission>((_, key) => key == "userId"),
                new FieldFilterFormatter<UserShopItem>((s, key) => key == "level" && s.level == 0),
                new FieldFilterFormatter<UserCostume3DStatus>((s, key) => key == "obtainedAt" && s.obtainedAt == 0 && s.status is "forbidden" or "sale"),
                new FieldFilterFormatter<UserChallengeLivePlayStatus>((s, key) => key == "playEndAt" && s.liveStatus == "start")
            },
            new[] { DumpSerializer.Options.Resolver }));

    public static byte[] Serialize(object? value) => value == null ? [0xc0] :
        MessagePackSerializer.Serialize(value.GetType(), value, Options);

    internal sealed class FriendProfileFormatter : IMessagePackFormatter<UserFriendProfile?>
    {
        public void Serialize(ref MessagePackWriter writer, UserFriendProfile? value, MessagePackSerializerOptions options)
        {
            var scoped = options.WithResolver(CompositeResolver.Create(
                new IMessagePackFormatter[] { new FieldFilterFormatter<UserCard>((_, key) =>
                    key is not ("cardId" or "level" or "masterRank" or "specialTrainingStatus" or "defaultImage")) },
                new[] { options.Resolver }));
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<UserFriendProfile>().Serialize(ref writer, value!, scoped);
        }

        public UserFriendProfile? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<UserFriendProfile>().Deserialize(ref reader, DumpSerializer.Options);
    }

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

    internal sealed class MissionReceiveFormatter : IMessagePackFormatter<UserMissionReceiveResponse?>
    {
        public void Serialize(ref MessagePackWriter writer, UserMissionReceiveResponse? value, MessagePackSerializerOptions options)
        {
            var scoped = options.WithResolver(CompositeResolver.Create(
                new IMessagePackFormatter[] { new FieldFilterFormatter<UserResource>((r, key) =>
                    (r.resourceType is "coin" or "jewel" or "material" && key == "resourceLevel" && r.resourceLevel == 0) ||
                    (r.resourceType is "coin" or "jewel" && key == "resourceId" && r.resourceId == 0)) },
                new[] { options.Resolver }));
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<UserMissionReceiveResponse>()
                .Serialize(ref writer, value!, scoped);
        }

        public UserMissionReceiveResponse? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<UserMissionReceiveResponse>().Deserialize(ref reader, DumpSerializer.Options);
    }

    internal sealed class CharacterMissionFormatter : IMessagePackFormatter<UserCharacterMissionV2Response?>
    {
        public void Serialize(ref MessagePackWriter writer, UserCharacterMissionV2Response? value, MessagePackSerializerOptions options)
        {
            if (value == null) { writer.WriteNil(); return; }
            writer.WriteMapHeader(2);
            writer.Write("updatedResources");
            MessagePackSerializer.Serialize(ref writer, value.updatedResources, options);
            writer.Write("reportedMissionStatuses");
            var scoped = options.WithResolver(CompositeResolver.Create(
                new IMessagePackFormatter[] { new FieldFilterFormatter<UserCharacterMissionV2Status>((_, key) => key == "userId") },
                new[] { options.Resolver }));
            MessagePackSerializer.Serialize(ref writer, value.reportedMissionStatuses, scoped);
        }

        public UserCharacterMissionV2Response? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<UserCharacterMissionV2Response>().Deserialize(ref reader, DumpSerializer.Options);
    }

    internal sealed class CostumeShopFormatter : IMessagePackFormatter<UserCostume3DShopResponse?>
    {
        public void Serialize(ref MessagePackWriter writer, UserCostume3DShopResponse? value, MessagePackSerializerOptions options)
        {
            var scoped = options.WithResolver(CompositeResolver.Create(
                new IMessagePackFormatter[] { new FieldFilterFormatter<UserResource>((r, key) =>
                    r.resourceType == "material" && key == "resourceLevel" && r.resourceLevel == 0) },
                new[] { options.Resolver }));
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<UserCostume3DShopResponse>()
                .Serialize(ref writer, value!, scoped);
        }

        public UserCostume3DShopResponse? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
            DumpSerializer.Options.Resolver.GetFormatterWithVerify<UserCostume3DShopResponse>().Deserialize(ref reader, DumpSerializer.Options);
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
