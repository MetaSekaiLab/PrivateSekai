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
        var master = new FriendMasterQueries(new MasterData(new MasterCacheConfig { PinTables = [] }, directory));
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
        var queries = new FriendQueries(operations);
        var request = new PostUserFriendRequest { message = "こんにちは", sentLocation = "id_search" };
        byte[] Request(long from, long to) => operations.ExecutePair(from, to, peer =>
        {
            Check.That(service.Request(peer, request) == 200, "申请成功");
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
        Request(2, 1);
        var approved = store.Read(1)!.Data.userFriends.Single();
        Check.That(approved.friendStatus == "friend" && store.Read(2)!.Data.userFriends.Single().friendStatus == "friend" &&
            approved.approvedAt == now && approved.message == "こんにちは" && approved.requestExpiredAt == sent.requestExpiredAt,
            "反向申请接受关系并保留原消息和期限");
        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Request(peer, request) == 409 && service.Approve(peer) == 409,
                "已是好友时重复申请和接受返回冲突");
            return null;
        });
        operations.ExecutePair(1, 3, peer =>
        {
            request.message = "api test";
            Check.That(service.Request(peer, request) == 400 && user.BuildRefresh().userFriends == null && peer.Data.userFriends == null,
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
            Check.That(service.Remove(peer, "release_friend") == 200, "删除好友成功");
            return queries.Project(user.BuildRefresh());
        });
        Check.That(DumpSerializer.Deserialize<SuiteUser>(removed).userFriends.Single().opponentUserId == 3 &&
            store.Read(2)!.Data.userFriends.Length == 0, "删除双方关系并保留其他好友，返回剩余全表");
        Request(1, 2);
        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Remove(peer, "cancel_friend_request") == 200, "发起方取消申请成功");
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userFriends.Single().opponentUserId == 3 && store.Read(2)!.Data.userFriends.Length == 0,
            "取消移除双方申请记录");
        Request(1, 2);
        operations.ExecutePair(2, 1, peer =>
        {
            Check.That(service.Remove(peer, "reject_friend_request") == 200, "接收方拒绝申请成功");
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userFriends.Single().opponentUserId == 3 && store.Read(2)!.Data.userFriends.Length == 0,
            "拒绝移除双方记录，不保留 rejected 状态");
        foreach (var type in new[] { "cancel_friend_request", "reject_friend_request", "release_friend" })
            operations.ExecutePair(1, 2, peer =>
            {
                Check.That(service.Remove(peer, type) == 200 && user.BuildRefresh().userFriends == null,
                    "无关系时删除类操作成功且不刷新好友列表");
                return null;
            });
    }
}
