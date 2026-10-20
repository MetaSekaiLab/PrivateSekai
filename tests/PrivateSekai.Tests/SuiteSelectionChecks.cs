extern alias game;

using System;
using System.Linq;
using System.Text.Json;
using game::Sekai;
using MessagePack;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class SuiteSelectionChecks
{
    public static void Run()
    {
        var state = TestUsers.Create(1);
        state.Data.userCards = [new() { cardId = 7, level = 3 }];
        state.Data.userReleaseConditions = [new() { releaseConditionId = 12 }];
        state.Data.userEventEpisodeStatuses = [new() { episodeId = 91, status = "unreleased" }];
        state.Data.userFriendInvitationCampaigns = [];
        state.Data.userFriendInvitationCampaignMissionRewardCounts = [];
        state.Data.userChallengeLivePlayDay = new();
        state.Data.userChallengeLivePlayStatuses = [];
        state.Data.userChallengeLiveSoloDecks = [];
        state.Data.userChallengeLiveSoloResults = [];
        state.Data.userChallengeLiveSoloStages = [];
        state.Data.userChallengeLiveSoloHighScoreRewards = [];
        state.Data.userMultiLivePenalty = new();
        state.Data.userBillingRefundPenalty = new();
        state.Data.userBoostReceived = new();
        state.Data.userColorfulPassV2 = new();
        state.Data.userMysekaiColorfulPass = new();
        state.Data.refreshableTypes = ["userGamedata"];
        state.Private.LoginStatus = new() { loginStatus = "offline" };
        var store = new MemoryUserStore();
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2, 700));
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        var user = new UserSession();
        var handler = new CountingRefresh();
        var operations = new UserOperation(store, new UserLocks(), user, TimeProvider.System, [handler]);
        JsonElement Read(params string[] names)
        {
            using var json = JsonDocument.Parse(MessagePackSerializer.ConvertToJson(
                operations.Query(1, () => user.BuildNamedSuite(names))));
            return json.RootElement.Clone();
        }

        var native = Read("user_release_condition", "userEventEpisodeStatuses");
        Check.That(native.GetProperty("userReleaseConditions")[0].GetProperty("releaseConditionId").GetInt32() == 12 &&
            !native.TryGetProperty("userEventEpisodeStatuses", out _) && !native.TryGetProperty("userCards", out _),
            "剧情原请求只返回已识别的解锁条件，不把驼峰名称猜为剧情选择组");
        Check.That(native.GetProperty("userChallengeLiveSoloDecks").GetArrayLength() == 0 &&
            native.GetProperty("userChallengeLivePlayDay").ValueKind == JsonValueKind.Object,
            "按名称读取保留挑战公共字段及空数组");
        Check.That(!native.GetProperty("userChallengeLivePlayDay").TryGetProperty("playDaysResetAt", out _),
            "挑战累计日尚无重置时间时省略零值字段");
        Check.That(native.GetProperty("refreshableTypes").GetArrayLength() == 0 && !native.TryGetProperty("userGamedata", out _),
            "按名称读取不夹带待刷新的玩家字段");
        var selected = Read("user_card", "user_release_condition", "user_card");
        Check.That(selected.GetProperty("userCards")[0].GetProperty("cardId").GetInt32() == 7 &&
            selected.EnumerateObject().Count(p => p.Name == "userCards") == 1,
            "逗号拆分后的多个组联合选择并去重");
        var basic = Read("user");
        Check.That(basic.TryGetProperty("userRegistration", out _) && basic.TryGetProperty("userGamedata", out _) &&
            !basic.TryGetProperty("userCards", out _), "user组返回注册信息和玩家数据");
        Check.That(!basic.GetProperty("userRegistration").TryGetProperty("yearOfBirth", out _) &&
            !basic.GetProperty("userRegistration").TryGetProperty("age", out _), "未设置的出生及年龄零值不进入响应");
        var invitation = Read("user_friend_invitation_campaign");
        Check.That(invitation.TryGetProperty("userFriendInvitationCampaigns", out _) &&
            invitation.TryGetProperty("userFriendInvitationCampaignMissionRewardCounts", out _),
            "邀请活动组同时返回活动和领奖次数");
        var unknown = Read("USER_RELEASE_CONDITION", "unknown", "", " user_card");
        Check.That(!unknown.TryGetProperty("userReleaseConditions", out _) && !unknown.TryGetProperty("userCards", out _) &&
            unknown.TryGetProperty("userPresents", out _), "未知名称与大小写不匹配保留公共字段，不回退全量");
        Check.That(Read().EnumerateObject().Select(p => p.Name).Order().SequenceEqual(
            unknown.EnumerateObject().Select(p => p.Name).Order()), "空名称列表只返回公共字段");
        var emptyStates = Read("user_multi_live_penalty", "user_billing_refund_penalty", "user_boost_received", "user_colorful_pass_v2", "user_mysekai_colorful_pass");
        Check.That(new[] { "userMultiLivePenalty", "userBillingRefundPenalty", "userBoostReceived", "userColorfulPassV2", "userMysekaiColorfulPass" }
            .All(key => !emptyStates.GetProperty(key).EnumerateObject().Any()), "未初始化的处罚、接收体力及通行证保持官方空对象形状");
        Check.That(handler.Calls == 0, "按名称读取不执行全量刷新处理器");
        Check.That(before.SequenceEqual(DumpSerializer.Serialize(store.Read(1)!.Data)) &&
            store.Read(1)!.Private.LoginStatus!.loginStatus == "offline" && store.Read(2)!.Data.userGamedata.coin == 700,
            "按名称查询不修改本人待刷新标记、私有状态或其他用户");
        var refresh = operations.Query(1, () => user.BuildRefresh(prepareRefresh: false));
        var normal = DumpSerializer.Deserialize<SuiteUser>(refresh);
        Check.That(normal.userChallengeLiveSoloDecks == null && normal.userGamedata != null,
            "Suite专用公共字段不扩张常规业务刷新响应");
        state.Data.userReleaseConditions = null;
        store.Save(1, state);
        Check.That(!Read("user_release_condition").TryGetProperty("userReleaseConditions", out _),
            "选择组无持有记录时保留模型的null省略规则");
        state.Data.userRegistration.yearOfBirth = 2000;
        state.Data.userRegistration.monthOfBirth = 1;
        state.Data.userRegistration.dayOfBirth = 2;
        state.Data.userRegistration.age = 26;
        state.Data.userChallengeLivePlayDay.playDaysResetAt = 100;
        state.Data.userMultiLivePenalty.penaltyEndAt = 101;
        state.Data.userBillingRefundPenalty.penaltyEndAt = 103;
        state.Data.userBoostReceived.resetAt = 102;
        state.Data.userColorfulPassV2.colorfulPassId = 1;
        state.Data.userMysekaiColorfulPass.mysekaiColorfulPassId = 1;
        store.Save(1, state);
        var positive = Read("user");
        Check.That(positive.GetProperty("userRegistration").GetProperty("yearOfBirth").GetInt32() == 2000 &&
            positive.GetProperty("userRegistration").GetProperty("monthOfBirth").GetInt32() == 1 &&
            positive.GetProperty("userRegistration").GetProperty("dayOfBirth").GetInt32() == 2 &&
            positive.GetProperty("userRegistration").GetProperty("age").GetInt32() == 26 &&
            positive.GetProperty("userChallengeLivePlayDay").GetProperty("playDaysResetAt").GetInt64() == 100,
            "响应省略规则保留所有非零出生信息、年龄及重置时间");
        var nonemptyStates = Read("user_multi_live_penalty", "user_billing_refund_penalty", "user_boost_received", "user_colorful_pass_v2", "user_mysekai_colorful_pass");
        Check.That(nonemptyStates.GetProperty("userMultiLivePenalty").GetProperty("penaltyEndAt").GetInt64() == 101 &&
            nonemptyStates.GetProperty("userBillingRefundPenalty").GetProperty("penaltyCount").GetInt32() == 0 &&
            nonemptyStates.GetProperty("userBoostReceived").GetProperty("receivedCount").GetInt32() == 0 &&
            nonemptyStates.GetProperty("userColorfulPassV2").GetProperty("continuousBuyHighTierCount").GetInt32() == 0 &&
            nonemptyStates.GetProperty("userMysekaiColorfulPass").GetProperty("expiredAt").GetUInt64() == 0,
            "非空状态保留有意义的零计数，不把空对象规则泛化为所有零值省略");
        Console.WriteLine("Suite选择：原始名称、组合、公共字段和只读隔离检查通过。");
    }

    private sealed class CountingRefresh : IUserRefreshHandler
    {
        public int Calls { get; private set; }
        public void PrepareRefresh() => Calls++;
    }
}
