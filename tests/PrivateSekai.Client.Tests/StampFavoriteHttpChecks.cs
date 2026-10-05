extern alias game;

using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class StampFavoriteHttpChecks
{
    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userStamps = [new() { stampId = 1 }, new() { stampId = 2 }];
        state.Data.UserStampFavoriteTabs = [];
        state.Data.userStampFavorites = [];
        store.Save(1, state);
        var step = new ScenarioStep { Operation = "stamp-favorite-save", Body = JsonNode.Parse("""
            {"userStampFavoriteResource":{"userStampFavoriteTabs":[{"tabNum":0,"tabName":"fixture-favorite-tab"}],
              "userStampFavorites":[{"stampId":1,"tabNum":0,"num":19},{"stampId":1,"tabNum":1,"num":5}]}}
            """)!.AsObject() };
        var scenario = new Scenario { Steps = [step] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "stamp-favorite"));
        var saved = store.Read(1)!.Data;
        check(saved.UserStampFavoriteTabs.Single().TabName == "fixture-favorite-tab" &&
            saved.userStampFavorites.Length == 2 && saved.userStampFavorites.All(f => f.userId == 1),
            "表情收藏 HTTP 保存页签名称、跨页签表情和最后槽位，并绑定目标账号");
        var record = File.ReadAllText(Path.Combine(directory, "stamp-favorite/001.json"));
        check(!record.Contains("fixture-favorite-tab") && !step.Body!.ToJsonString().Contains("userId"),
            "表情页签名称脱敏，发送不修改共享场景");
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "stamp-favorite-repeat"));
        var response = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "stamp-favorite-repeat/001.json")))!["response"]!["updatedResources"]!;
        check(response["userStampFavorites"] == null && response["userStampFavoriteTabs"] == null,
            "重复保存同一收藏省略未变化的资源字段");
        var foreign = step.Body!.DeepClone().AsObject();
        foreign["userStampFavoriteResource"]!["userStampFavorites"]![0]!["userId"] = 2;
        var rejected = false;
        try { await client.Send(new() { Operation = "stamp-favorite-save", Body = foreign }); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && client.LastHttpStatus == null, "表情收藏发送前拒绝其他账号记录");
    }
}
