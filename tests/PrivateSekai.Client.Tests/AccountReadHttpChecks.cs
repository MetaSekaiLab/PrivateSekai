using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class AccountReadHttpChecks
{
    public static void WriteTemplates(string directory)
    {
        File.WriteAllText(Path.Combine(directory, "api_user_auth.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "api_system.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "user_0.json"), """{"userRegistration":{"userId":0},"userGamedata":{"userId":0}}""");
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userEventBreakTime = new() { lastDecreaseAt = 0 };
        store.Save(1, state);
        var scenario = new Scenario { Steps =
        [
            new() { Operation = "suite-friends", Expect = new() { ["/userFriends"] = new JsonArray() } },
            new() { Operation = "suite-break-time" },
            new() { Operation = "account-restrict-info", Expect = new() { ["/isRestrictDeviceTransfer"] = JsonValue.Create(false) } }
        ] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "account-read"));
        var friends = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "account-read/001.json")))!["response"]!.AsObject();
        var breakTime = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "account-read/002.json")))!["response"]!.AsObject();
        check(friends.ContainsKey("userFriends") && !friends.ContainsKey("userCards") && !friends.ContainsKey("userRegistration"),
            "真实 Suite parts 控制器按名称返回好友窄字段集");
        check(breakTime.ContainsKey("userEventBreakTime") && !breakTime.ContainsKey("userFriends"),
            "休息时间片段与好友片段独立读取");
        check(JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "account-read/003.json")))!["status"]!.GetValue<string>() == "completed",
            "账号限制信息通过真实控制器和加密响应读取");
        state = store.Read(1)!;
        state.Data.userEventBreakTime = null;
        store.Save(1, state);
        var missing = await client.Send(new() { Operation = "suite-break-time" });
        check(missing.ContainsKey("now") && !missing.ContainsKey("userEventBreakTime"), "局部字段无数据时省略，不误判为协议错误");
    }
}
