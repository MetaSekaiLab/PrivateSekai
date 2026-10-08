extern alias game;

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Home;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class FriendChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "friend-fixture");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "configs.json"), """
            [{"configKey":"friend_request_message_length_limit","value":"30"},
             {"configKey":"friend_request_expire_hour","value":"168"},
             {"configKey":"friend_count_limit","value":"300"}]
            """);
        File.WriteAllText(Path.Combine(directory, "ngWords.json"), """[{"id":1,"word":"test"}]""");
        File.WriteAllText(Path.Combine(directory, "honors.json"), """[{"id":1},{"id":2,"honorMissionType":"fixture_mission"}]""");
        var masterData = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
        var master = new FriendMasterQueries(masterData);
        var store = new MemoryUserStore();
        for (var id = 1; id <= 3; id++)
        {
            var state = TestUsers.Create(id);
            state.Data.userConfig = new() { friendRequestScope = "all", isDisplayLoginStatus = true };
            state.Data.userProfile = new() { profileImageType = "leader" };
            state.Data.userGamedata.deck = 1;
            state.Data.userDecks = [new() { deckId = 1, leader = 1 }];
            state.Data.userCards = [new() { cardId = 1, level = 7, masterRank = 0,
                specialTrainingStatus = "not_doing", defaultImage = "original", exp = 999 }];
            state.Private.LoginStatus = new() { loginStatus = "online", loginStatusUpdatedAt = 123 };
            store.Save(id, state);
        }
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var service = new FriendService(user, master);
        var queries = new FriendQueries(operations, masterData);
        var request = new PostUserFriendRequest { message = "こんにちは", sentLocation = "id_search" };
        byte[] Request(long from, long to) => operations.ExecutePair(from, to, peer =>
        {
            Check.That(service.Request(peer, request).Status == 200, "申请成功");
            return queries.Project(user.BuildRefresh());
        });
        Check.Throws<MessagePackSerializationException>(() => operations.ExecutePair(1, 2, peer =>
        {
            service.Request(peer, request);
            return new BrokenResponse();
        }), "申请编码失败回滚双方关系");
        Check.That(store.Read(1)!.Data.userFriends == null && store.Read(2)!.Data.userFriends == null,
            "申请失败后双方均无关系");
        var first = Request(1, 2);
        var sent = store.Read(1)!.Data.userFriends.Single();
        var pending = store.Read(2)!.Data.userFriends.Single();
        var now = DumpSerializer.Deserialize<SuiteUser>(first).now;
        Check.That(sent.friendStatus == "sent_request" && pending.friendStatus == "pending_request" &&
            sent.message == request.message && pending.message == request.message &&
            sent.requestExpiredAt == now + 604800000 && pending.requestExpiredAt == sent.requestExpiredAt,
            "双方申请方向、消息和七天期限一致");
        Check.That(sent.opponentUserFriendProfile == null && sent.userLoginStatus == null,
            "响应投影不写入持久关系");
        var json = JsonNode.Parse(MessagePackSerializer.ConvertToJson(first))!;
        var friend = json["userFriends"]![0]!.AsObject();
        var card = friend["opponentUserFriendProfile"]!["userCard"]!.AsObject();
        Check.That(!friend.ContainsKey("approvedAt") && card.Count == 5 && !card.ContainsKey("exp"),
            "申请省略接受时间，好友卡牌只输出五个字段");
        Check.That(card["level"]!.GetValue<int>() == 7 && friend["userLoginStatus"]!["loginStatus"]!.GetValue<string>() == "online",
            "好友资料来自对方当前快照");
        var avatar = store.Read(2)!;
        avatar.Data.userCards = [.. avatar.Data.userCards, new() { cardId = 2, level = 1 }];
        avatar.Data.userProfile.profileImageType = "card_before_special_training";
        avatar.Data.userProfile.profileImageId = 2;
        avatar.Data.userProfileHonors = [new() { seq = 1, profileHonorType = "normal", honorId = 1, honorLevel = 1,
            bondsHonorViewType = "none", honorBackgroundId = 10101, honorWordId = 10101 }];
        avatar.Data.userHonorMissions = [new() { honorMissionType = "fixture_mission", progress = 42 }];
        store.Save(2, avatar);
        var projection = queries.Project(store.Read(1)!.Data).userFriends.Single().opponentUserFriendProfile;
        Check.That(projection.userProfile.profileImageId == 2 && projection.userProfile.profileImageType == "card_before_special_training" &&
            projection.userCard.cardId == 1 && projection.userCard.level == 7,
            "自选头像保留选中卡牌 ID，好友卡牌仍取主队队长");
        Check.That(projection.userProfileHonors.Single().honorBackgroundId == 10101 &&
            projection.userProfileHonors.Single().honorWordId == 10101 && projection.userHonorMissions.Length == 0,
            "普通称号保留背景与文字，不泄露无关称号任务");
        projection.userProfileHonors[0].honorLevel = 9;
        Check.That(store.Read(2)!.Data.userProfileHonors[0].honorLevel == 1,
            "好友称号投影与对方存档引用隔离");
        avatar.Data.userProfileHonors = [new() { seq = 1, profileHonorType = "normal", honorId = 2, honorLevel = 1 }];
        avatar.Data.userHonorMissions = [new() { userId = 2, honorMissionType = "fixture_mission", progress = 42, achievedMissionIds = [123] },
            new() { honorMissionType = "unrelated", progress = 99 }];
        store.Save(2, avatar);
        projection = queries.Project(store.Read(1)!.Data).userFriends.Single().opponentUserFriendProfile;
        Check.That(projection.userHonorMissions.Length == 1 && projection.userHonorMissions[0].progress == 42 &&
            projection.userHonorMissions[0].achievedMissionIds == null && projection.userHonorMissions[0].userId == 0,
            "好友称号仅映射装备对应的任务类型与进度，不输出达成提示或账号字段");
        projection.userHonorMissions[0].progress = 0;
        Check.That(store.Read(2)!.Data.userHonorMissions[0].progress == 42,
            "好友任务进度投影不修改对方存档");
        avatar.Data.userProfileHonors = [];
        store.Save(2, avatar);
        Check.That(queries.Project(store.Read(1)!.Data).userFriends.Single().opponentUserFriendProfile.userHonorMissions.Length == 0,
            "移除任务称号后好友资料不再返回任务进度");
        foreach (var id in new long[] { 1, 2 })
        {
            var state = store.Read(id)!;
            state.Data.userFriends.Single().requestExpiredAt -= 1000;
            store.Save(id, state);
        }
        Request(1, 2);
        Check.That(store.Read(1)!.Data.userFriends.Length == 1 && store.Read(2)!.Data.userFriends.Length == 1 &&
            store.Read(2)!.Data.userFriends.Single().requestExpiredAt == sent.requestExpiredAt,
            "重复申请更新双方期限，不追加重复关系");
        request.message = "";
        Request(1, 2);
        Check.That(store.Read(1)!.Data.userFriends.Single().message == null && store.Read(2)!.Data.userFriends.Single().message == null,
            "重发空消息清除双方原消息");
        request.message = "こんにちは";
        Request(1, 2);
        Check.That(store.Read(1)!.Data.userFriends.Single().message == request.message && store.Read(2)!.Data.userFriends.Single().message == request.message,
            "重发非空消息更新双方文本");
        request.message = "";
        Request(2, 1);
        var approved = store.Read(1)!.Data.userFriends.Single();
        Check.That(approved.friendStatus == "friend" && store.Read(2)!.Data.userFriends.Single().friendStatus == "friend" &&
            approved.approvedAt == now && approved.message == "こんにちは" && approved.requestExpiredAt == sent.requestExpiredAt,
            "反向申请接受关系并保留原消息和期限");
        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Request(peer, request).Status == 409 && service.Approve(peer) == 409,
                "已是好友时重复申请和接受返回冲突");
            return null;
        });
        operations.ExecutePair(1, 3, peer =>
        {
            request.message = "api test";
            Check.That(service.Request(peer, request).Status == 400 && user.BuildRefresh().userFriends == null && peer.Data.userFriends == null,
                "NG 消息拒绝且不更改双方关系");
            return null;
        });
        request.message = "";
        var second = JsonNode.Parse(MessagePackSerializer.ConvertToJson(Request(1, 3)))!;
        Check.That(second["userFriends"]!.AsArray().Count == 2 && !second["userFriends"]![1]!.AsObject().ContainsKey("message"),
            "新申请返回完整好友列表并省略空消息");
        Check.Throws<MessagePackSerializationException>(() => operations.ExecutePair(3, 1, peer =>
        {
            service.Approve(peer);
            return new BrokenResponse();
        }), "接受编码失败回滚双方");
        Check.That(store.Read(3)!.Data.userFriends.Single().friendStatus == "pending_request" &&
            store.Read(1)!.Data.userFriends.Single(f => f.opponentUserId == 3).friendStatus == "sent_request",
            "接受回滚保留双方申请状态");
        operations.ExecutePair(3, 1, peer =>
        {
            Check.That(service.Approve(peer) == 200, "正常接受申请成功");
            return queries.Project(user.BuildRefresh());
        });
        Check.That(store.Read(3)!.Data.userFriends.Single().approvedAt == now &&
            store.Read(1)!.Data.userFriends.Single(f => f.opponentUserId == 3).approvedAt == now,
            "正常接受写入双方相同时间");
        Check.Throws<MessagePackSerializationException>(() => operations.ExecutePair(1, 2, peer =>
        {
            service.Remove(peer, "release_friend");
            return new BrokenResponse();
        }), "删除好友编码失败回滚双方");
        Check.That(store.Read(1)!.Data.userFriends.Length == 2 && store.Read(2)!.Data.userFriends.Length == 1,
            "删除失败不丢失关系");
        var removed = operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Remove(peer, "release_friend").Status == 200, "删除好友成功");
            return queries.Project(user.BuildRefresh());
        });
        Check.That(DumpSerializer.Deserialize<SuiteUser>(removed).userFriends.Single().opponentUserId == 3 &&
            store.Read(2)!.Data.userFriends.Length == 0, "删除双方关系并保留其他好友，返回剩余全表");
        Request(1, 2);
        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Remove(peer, "cancel_friend_request").Status == 200, "发起方取消申请成功");
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userFriends.Single().opponentUserId == 3 && store.Read(2)!.Data.userFriends.Length == 0,
            "取消移除双方申请记录");
        Request(1, 2);
        operations.ExecutePair(2, 1, peer =>
        {
            Check.That(service.Remove(peer, "reject_friend_request").Status == 200, "接收方拒绝申请成功");
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userFriends.Single().opponentUserId == 3 && store.Read(2)!.Data.userFriends.Length == 0,
            "拒绝移除双方记录，不保留 rejected 状态");
        foreach (var type in new[] { "cancel_friend_request", "reject_friend_request", "release_friend" })
            operations.ExecutePair(1, 2, peer =>
            {
                Check.That(service.Remove(peer, type).Status == 200 && user.BuildRefresh().userFriends == null,
                    "无关系时删除类操作成功且不刷新好友列表");
                return null;
            });
        var restricted = store.Read(2)!;
        restricted.Data.userConfig.friendRequestScope = "reject";
        store.Save(2, restricted);
        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Request(peer, request) == (409, "opponent_friend_request_scope_reject") &&
                user.BuildRefresh().userFriends == null && peer.Data.userFriends.Length == 0,
                "拒收范围返回明确错误码且不新增关系");
            return null;
        });
        restricted = store.Read(2)!;
        restricted.Data.userConfig.friendRequestScope = "id_search";
        store.Save(2, restricted);
        request.sentLocation = "multi_live";
        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Request(peer, request) == (409, "opponent_friend_request_scope_id_search") &&
                user.BuildRefresh().userFriends == null && peer.Data.userFriends.Length == 0,
                "仅 ID 搜索范围拒绝 multi_live 来源且不新增关系");
            return null;
        });
        request.sentLocation = "id_search";
        Request(1, 2);
        Check.That(store.Read(2)!.Data.userFriends.Single().friendStatus == "pending_request",
            "仅 ID 搜索范围接受匹配来源");
        foreach (var id in new long[] { 1, 2 })
        {
            var state = store.Read(id)!;
            state.Data.userConfig.friendRequestScope = "reject";
            store.Save(id, state);
        }
        foreach (var pair in new[] { (From: 1L, To: 2L), (From: 2L, To: 1L) })
            operations.ExecutePair(pair.From, pair.To, peer =>
            {
                Check.That(service.Request(peer, request) == (409, "opponent_friend_request_scope_reject") &&
                    user.BuildRefresh().userFriends == null, "范围变更拒绝已有申请的重发及反向申请");
                return null;
            });
        Check.That(store.Read(1)!.Data.userFriends.Single(f => f.opponentUserId == 2).friendStatus == "sent_request" &&
            store.Read(2)!.Data.userFriends.Single().friendStatus == "pending_request", "被范围拒绝后保留双方申请方向");
        operations.ExecutePair(2, 1, peer =>
        {
            Check.That(service.Approve(peer) == 200, "直接 PUT 接受不受双方拒收新申请的配置影响");
            return user.BuildRefresh();
        });
        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Request(peer, request) == (409, "opponent_friend_request_scope_reject") &&
                user.BuildRefresh().userFriends == null, "已有好友时范围拒绝优先于空错误码的重复申请冲突");
            return null;
        });
        restricted = store.Read(2)!;
        restricted.Data.userConfig.friendRequestScope = "id_search";
        store.Save(2, restricted);
        request.sentLocation = "multi_live";
        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Request(peer, request) == (409, "opponent_friend_request_scope_id_search") &&
                user.BuildRefresh().userFriends == null, "已有好友时来源范围错误优先于关系冲突");
            return null;
        });
        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Remove(peer, "cancel_friend_request").Status == 200, "好友状态下取消申请仍删除关系");
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userFriends.Single().opponentUserId == 3 && store.Read(2)!.Data.userFriends.Length == 0,
            "取消好友关系移除双方并保留其他好友");
        request.sentLocation = "id_search";
        Request(1, 2);
        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Remove(peer, "reject_friend_request") == (409, "") && user.BuildRefresh().userFriends == null,
                "发起方拒绝自己的申请返回冲突且不刷新");
            return null;
        });
        Check.That(store.Read(1)!.Data.userFriends.Single(f => f.opponentUserId == 2).friendStatus == "sent_request" &&
            store.Read(2)!.Data.userFriends.Single().friendStatus == "pending_request", "错方向拒绝保留双方申请");
        foreach (var item in new[] { (From: 1L, To: 2L, Type: "release_friend", Error: ""),
            (From: 2L, To: 1L, Type: "cancel_friend_request", Error: ""),
            (From: 2L, To: 1L, Type: "release_friend", Error: "exists_pending_friend_request") })
            operations.ExecutePair(item.From, item.To, peer =>
            {
                Check.That(service.Remove(peer, item.Type) == (409, item.Error) && user.BuildRefresh().userFriends == null,
                    "申请状态中的不匹配删除返回对应错误码且不刷新");
                return null;
            });
        Check.That(store.Read(1)!.Data.userFriends.Single(f => f.opponentUserId == 2).friendStatus == "sent_request" &&
            store.Read(2)!.Data.userFriends.Single().friendStatus == "pending_request", "不匹配删除不改变双方申请");
        operations.ExecutePair(2, 1, peer => { service.Approve(peer); return user.BuildRefresh(); });
        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Remove(peer, "reject_friend_request") == (409, "") && user.BuildRefresh().userFriends == null,
                "好友状态下拒绝动作返回空错误码冲突");
            return null;
        });
        Check.That(store.Read(1)!.Data.userFriends.Single(f => f.opponentUserId == 2).friendStatus == "friend" &&
            store.Read(2)!.Data.userFriends.Single().friendStatus == "friend", "好友状态下拒绝失败保留双方关系");
        var missionHonor = store.Read(2)!;
        missionHonor.Data.userProfileHonors = [new() { seq = 1, profileHonorType = "bonds", honorId = 2 }];
        store.Save(2, missionHonor);
        Check.Throws<NotSupportedException>(() => queries.Project(store.Read(1)!.Data),
            "未采样的羁绊称号不伪造普通称号映射");
    }
}
