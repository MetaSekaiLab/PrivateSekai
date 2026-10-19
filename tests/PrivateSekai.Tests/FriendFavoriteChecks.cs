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

internal static class FriendFavoriteChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "friend-favorite-fixture");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "configs.json"), """
            [{"configKey":"friend_request_message_length_limit","value":"30"},
             {"configKey":"friend_request_expire_hour","value":"168"},
             {"configKey":"friend_count_limit","value":"300"}]
            """);
        File.WriteAllText(Path.Combine(directory, "ngWords.json"), "[]");
        var store = new MemoryUserStore();
        for (var id = 1; id <= 6; id++)
        {
            var state = TestUsers.Create(id);
            state.Data.userConfig = new() { friendRequestScope = "all" };
            if (id == 1)
                state.Data.userFriends = [
                    new() { opponentUserId = 2, friendStatus = "friend", approvedAt = 10, message = "fixture" },
                    new() { opponentUserId = 3, friendStatus = "friend", approvedAt = 20 },
                    new() { opponentUserId = 4, friendStatus = "pending_request", requestExpiredAt = 30 },
                    new() { opponentUserId = 5, friendStatus = "sent_request", requestExpiredAt = 40 }];
            if (id == 2) state.Data.userFriends = [new() { opponentUserId = 1, friendStatus = "friend" }];
            store.Save(id, state);
        }
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var master = new FriendMasterQueries(new MasterData(new MasterCacheConfig { PinTables = [] }, directory));
        var service = new FriendService(user, master);
        SuiteUser Save(params long[] ids) => DumpSerializer.Deserialize<SuiteUser>(operations.Execute(1, () =>
        {
            service.SaveFavorites(new() { favoriteUserIds = ids.ToList() });
            return user.BuildRefresh();
        }));
        long[] Favorites() => store.Read(1)!.Data.userFriends.Where(friend => friend.isFavorite)
            .Select(friend => friend.opponentUserId).ToArray();

        var first = Save(2);
        Check.That(Favorites().SequenceEqual(new long[] { 2 }) && first.userFriends.Length == 4,
            "收藏已确认好友，刷新保留全部关系记录");
        Check.That(!store.Read(2)!.Data.userFriends.Single().isFavorite,
            "收藏只修改本人列表，不改变对方收藏状态");
        Check.That(first.userFriends[0].message == "fixture" && first.userFriends[0].approvedAt == 10 &&
            first.userFriends[2].requestExpiredAt == 30 && first.userFriends[3].requestExpiredAt == 40,
            "收藏保留申请消息、关系方向及业务时间");
        Check.That(Save(2).userFriends == null, "重复收藏不刷新未变化的好友列表");
        Save(3, 2);
        Check.That(Favorites().SequenceEqual(new long[] { 2, 3 }), "批量收藏保留原好友顺序");
        Check.That(Save(2, 3, 2).userFriends == null, "收藏顺序和重复编号不产生额外记录或刷新");
        Save(3);
        Check.That(Favorites().SequenceEqual(new long[] { 3 }), "收藏全量替换并取消未提交的好友收藏");
        Save(2, 4, 5, 1, 0, -1, long.MaxValue);
        Check.That(Favorites().SequenceEqual(new long[] { 2 }) && store.Read(1)!.Data.userFriends.Length == 4,
            "忽略申请中、本人和未知编号，不创建额外关系");
        Save();
        Check.That(Favorites().Length == 0 && Save().userFriends == null, "空列表清空收藏，重复清空无额外刷新");
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            service.SaveFavorites(new() { favoriteUserIds = [2, 3] });
            return new BrokenResponse();
        }), "收藏响应编码失败回滚全部变更");
        Check.That(Favorites().Length == 0, "编码失败后未保留任何收藏");
        var peerBefore = DumpSerializer.Serialize(store.Read(2)!.Data);
        Save(2);
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peerBefore),
            "收藏不改变对方任何持久状态");

        operations.ExecutePair(1, 2, peer =>
        {
            Check.That(service.Remove(peer, "release_friend").Status == 200, "收藏的好友可以正常删除");
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userFriends.All(friend => friend.opponentUserId != 2) &&
            store.Read(2)!.Data.userFriends.Length == 0, "删除关系同时移除对应收藏记录");
        Check.That(Save(2).userFriends == null && Favorites().Length == 0, "已删除的好友不会因收藏请求重新出现");
        operations.ExecutePair(1, 2, peer =>
        {
            service.Request(peer, new() { message = "", sentLocation = "id_search" });
            return user.BuildRefresh();
        });
        operations.ExecutePair(2, 1, peer =>
        {
            service.Approve(peer);
            return user.BuildRefresh();
        });
        Check.That(!store.Read(1)!.Data.userFriends.Single(friend => friend.opponentUserId == 2).isFavorite,
            "重新成为好友后不恢复旧收藏标记");
        var empty = DumpSerializer.Deserialize<SuiteUser>(operations.Execute(6, () =>
        {
            service.SaveFavorites(new() { favoriteUserIds = [2] });
            return user.BuildRefresh();
        }));
        Check.That(store.Read(6)!.Data.userFriends == null && empty.userFriends == null,
            "没有好友时收藏请求保持原状态和窄刷新集");
        var maximum = store.Read(1)!;
        maximum.Data.userFriends = Enumerable.Range(1000, 300)
            .Select(id => new UserFriend { opponentUserId = id, friendStatus = "friend" }).ToArray();
        store.Save(1, maximum);
        Save(Enumerable.Range(1000, 300).Select(id => (long)id).ToArray());
        Check.That(Favorites().Length == 300, "收藏支持完整好友列表，未限制为少量样本编号");
        var common = store.Read(1)!;
        common.Data.userGachaCeilExchanges = [new() { exchangeStatus = "exchangeable" },
            new() { exchangeStatus = "exchangeable", exchangeRemaining = 10 }];
        common.Data.userHomeBanners = [new(), new() { fromUserRank = 1, toUserRank = 10, transitionDestinationId = 20 }];
        common.Data.userRankMatchResult = new() { rankMatchSeasonId = 0 };
        store.Save(1, common);
        var response = JsonNode.Parse(MessagePackSerializer.ConvertToJson(operations.Execute(1, () =>
        {
            service.SaveFavorites(new() { favoriteUserIds = [] });
            return new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() };
        })))!["updatedResources"]!;
        Check.That(response["userGachaCeilExchanges"]![0]!["exchangeRemaining"] == null &&
            response["userGachaCeilExchanges"]![1]!["exchangeRemaining"]!.GetValue<int>() == 10,
            "收藏响应的通用兑换资源省略零值余量并保留非零余量");
        Check.That(response["userHomeBanners"]![0]!["fromUserRank"] == null &&
            response["userHomeBanners"]![0]!["toUserRank"] == null &&
            response["userHomeBanners"]![0]!["transitionDestinationId"] == null &&
            response["userHomeBanners"]![1]!["fromUserRank"]!.GetValue<int>() == 1 &&
            response["userHomeBanners"]![1]!["toUserRank"]!.GetValue<int>() == 10 &&
            response["userHomeBanners"]![1]!["transitionDestinationId"]!.GetValue<int>() == 20,
            "收藏响应横幅省略未设置字段并保留明确非零值");
        Check.That(response["userRankMatchResult"]!["rankMatchSeasonId"] == null,
            "收藏响应未参与排位时省略零值赛季编号");
        common.Data.userRankMatchResult.rankMatchSeasonId = 7;
        store.Save(1, common);
        var season = JsonNode.Parse(MessagePackSerializer.ConvertToJson(operations.Query(1, () => user.BuildRefresh())))!;
        Check.That(season["userRankMatchResult"]!["rankMatchSeasonId"]!.GetValue<int>() == 7,
            "通用排位资源保留非零赛季编号");
    }
}
