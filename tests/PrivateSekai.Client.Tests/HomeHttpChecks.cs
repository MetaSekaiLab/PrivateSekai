using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class HomeHttpChecks
{
    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.unreadUserTopics = [new() { topicId = 1 }, new() { topicId = 2 }];
        state.Data.viewableAppeal = new() { appealIds = [3] };
        state.Data.userFriends = [];
        state.Data.userNews = [];
        state.Data.userLoginBonuses = [];
        store.Save(1, state);
        // 公告入口使用服务端模板账号快照。
        var template = store.Read(1)!;
        template.Data.userRegistration.userId = 0;
        store.Save(0, template);
        var scenario = new Scenario { Steps =
        [
            new() { Operation = "information", Expect = new() { ["/informations"] = new JsonArray() } },
            new() { Operation = "home-refresh" },
            new() { Operation = "home-refresh", Body = JsonNode.Parse("""{"refreshableTypes":[]}""")!.AsObject() },
            new() { Operation = "topic-read", Args = new() { ["topicId"] = "1" } },
            new() { Operation = "appeal-read", Body = JsonNode.Parse("""{"appealIds":[3,4,4]}""")!.AsObject() }
        ] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "home"));
        var saved = store.Read(1)!.Data;
        check(saved.unreadUserTopics.Single().topicId == 2, "话题已读仅移除对应未读项");
        check(saved.viewableAppeal.appealIds.SequenceEqual(new[] { 3, 4 }), "提示已读合并去重且保留已有项");
        var empty = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "home/002.json")))!;
        var body = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "home/003.json")))!;
        check(empty["status"]!.GetValue<string>() == "completed" && empty["request"] == null &&
            body["status"]!.GetValue<string>() == "completed" && body["request"]!["refreshableTypes"]!.AsArray().Count == 0,
            "首页刷新兼容空 body 和 typed body，两种请求均经真实 HTTP 验证");
        await client.Send(new() { Operation = "home-refresh" });
        check(client.LastLoginBonusStatus == "true",
            "没有登录奖励记录时首页响应提示首次可领取");
        state = store.Read(1)!;
        state.Data.userLoginBonuses = [new() { loginBonusId = 1, loginBonusType = "normal", progress = 1 }];
        store.Save(1, state);
        var repeated = await client.Send(new() { Operation = "home-refresh",
            Body = JsonNode.Parse("""{"refreshableTypes":["login_bonus"]}""")!.AsObject() });
        check(client.LastLoginBonusStatus == "false" && repeated["userLoginBonuses"]!.AsArray().Count == 0,
            "已领取时奖励刷新返回 false 状态头和空奖励列表");
        await client.Suite();
        check(client.LastLoginBonusStatus == "false", "已领取时完整 Suite 返回 false 状态头");
    }
}
