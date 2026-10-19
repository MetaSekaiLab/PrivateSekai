extern alias game;

using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class FriendFavoriteHttpChecks
{
    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.refreshableTypes = [];
        state.Data.userFriends = [new() { opponentUserId = 901, friendStatus = "friend" },
            new() { opponentUserId = 902, friendStatus = "friend" },
            new() { opponentUserId = 903, friendStatus = "pending_request" }];
        store.Save(1, state);
        ScenarioStep Save(params long[] ids) => new()
        {
            Operation = "friend-favorite-save",
            Body = new JsonObject { ["favoriteUserIds"] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) }
        };
        var scenario = new Scenario { Steps = [Save(901), Save(901), Save(902, 901, 901), Save()] };
        var output = Path.Combine(directory, "friend-favorite");
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, output);
        JsonNode Record(int index) => JsonNode.Parse(File.ReadAllText(Path.Combine(output, $"{index:D3}.json")))!;
        var first = Record(1);
        check(first["response"]!["updatedResources"]!["userFriends"]!.AsArray().Count == 3 &&
            first["after"]!["userFriends"]![0]!["isFavorite"]!.GetValue<bool>(),
            "好友收藏通过真实控制器及加密协议保存，并能通过完整Suite回读");
        check(Record(2)["response"]!["updatedResources"]!["userFriends"] == null,
            "重复收藏 HTTP 返回省略未变化的好友资源");
        var bulk = Record(3)["after"]!["userFriends"]!.AsArray();
        check(bulk.Select(friend => friend!["opponentUserId"]!.GetValue<long>()).SequenceEqual(new long[] { 901, 902, 903 }) &&
            bulk.Count(friend => friend!["isFavorite"]!.GetValue<bool>()) == 2,
            "好友批量收藏按集合处理重复编号，保留列表顺序并忽略申请记录");
        check(store.Read(1)!.Data.userFriends.All(friend => !friend.isFavorite),
            "好友清空收藏通过 HTTP 持久化，保留所有关系");
        check(first["request"]!["favoriteUserIds"]![0]!.GetValue<string>() == "<redacted>" &&
            scenario.Steps[0].Body!["favoriteUserIds"]![0]!.GetValue<long>() == 901,
            "好友收藏 HTTP 捕获隐藏目标编号，实际请求对象保持完整");
        var beforeInvalid = DumpSerializer.Serialize(store.Read(1)!.Data);
        foreach (var body in new[] { new JsonObject(), new JsonObject { ["favoriteUserIds"] = null } })
        {
            using var invalidClient = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalidClient.Send(new() { Operation = "system" });
            var invalid = false;
            try { await invalidClient.Send(new() { Operation = "friend-favorite-save", Body = body }); }
            catch (ClientFailure) { invalid = true; }
            check(invalid && invalidClient.LastHttpStatus == 400 &&
                invalidClient.LastResponse?["favoriteUserIds"]?.GetValue<string>() == "NotNull",
                "缺失或null收藏列表返回官方400校验结构");
        }
        check(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(beforeInvalid),
            "收藏校验失败不改变任何持久状态或触发任务刷新");
        await client.Send(new() { Operation = "system" });
        var official = new TargetConfiguration { Kind = "official", BaseUrl = "https://example.invalid" };
        var rejected = false;
        try { ScenarioRunner.Validate(scenario, [official], new HashSet<string>()); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected, "好友收藏的官方写操作需要显式启用");
        store.Save(1, original);
    }
}
