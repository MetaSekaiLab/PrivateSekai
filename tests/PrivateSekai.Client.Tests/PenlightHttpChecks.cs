extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class PenlightHttpChecks
{
    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userPenlights = [new() { penlightId = 1, favoriteFlg = true }, new() { penlightId = 7 }, new() { penlightId = 11 }];
        state.Data.userVirtualShops = [new() { virtualShopId = 9, userVirtualShopItems =
            [new() { virtualShopId = 9, virtualShopItemId = 99, status = "sale" }] }];
        store.Save(1, state);
        ScenarioStep Save(int id, bool flag = true) => new()
        {
            Operation = "penlight-save", Args = new() { ["penlightId"] = id.ToString() }, Body = new() { ["favoriteFlg"] = flag }
        };
        var scenario = new Scenario { Steps = [Save(11), Save(11), Save(11, false), Save(1, false), Save(1)] };
        var output = Path.Combine(directory, "penlight");
        await ScenarioRunner.Run(client, scenario, output);
        JsonNode Record(int index) => JsonNode.Parse(File.ReadAllText(Path.Combine(output, $"{index:D3}.json")))!;
        var first = Record(1)["response"]!["updatedResources"]!["userPenlights"]!.AsArray();
        check(first.Count == 3 && !first[0]!["favoriteFlg"]!.GetValue<bool>() && first[2]!["favoriteFlg"]!.GetValue<bool>(),
            "荧光棒通过加密HTTP切换选择，返回完整持有列表并保留false标记");
        check(Record(1)["response"]!["updatedResources"]!["userVirtualShops"]![0]!["userVirtualShopItems"]![0]!["status"]!.GetValue<string>() == "sale",
            "荧光棒选择变化同时返回完整商店，保留原商品状态");
        check(Enumerable.Range(2, 3).All(i => Record(i)["response"]!["updatedResources"]!["userPenlights"] == null &&
            Record(i)["response"]!["updatedResources"]!["userVirtualShops"] == null),
            "重复选择及已选或未选的false请求都省略荧光棒和商店刷新");
        check(Record(3)["after"]!["userPenlights"]![2]!["favoriteFlg"]!.GetValue<bool>() &&
            store.Read(1)!.Data.userPenlights.Single(p => p.favoriteFlg).penlightId == 1,
            "false不取消选择，切回默认后独立Suite保持唯一默认选择");
        check(!scenario.Steps[0].Body!.ContainsKey("userId") && !scenario.Steps[0].Body!.ContainsKey("penlightId") &&
            Operations.All["penlight-save"].Method == "PATCH" && Operations.Path(Operations.All["penlight-save"], scenario.Steps[0], 1) == "/api/user/1/penlight/11",
            "荧光棒PATCH使用路径编号，运行时填入body编号和账号而不改场景");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        foreach (var flag in new[] { true, false })
        {
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var failed = false;
            try { await invalid.Send(Save(999, flag)); } catch (ClientFailure) { failed = true; }
            check(failed && invalid.LastHttpStatus == 404 && invalid.LastResponse?["errorCode"]?.GetValue<string>() == "" &&
                invalid.LastResponse?["errorMessage"]?.GetValue<string>() == "", "未知荧光棒的true和false均通过HTTP返回404空错误码");
        }
        await client.Send(new() { Operation = "system" });
        foreach (var field in new[] { "userId", "penlightId" })
        {
            var wrong = Save(11);
            wrong.Body![field] = 2;
            var rejected = false;
            try { await client.Send(wrong); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && client.LastHttpStatus == null, "荧光棒Client在发出HTTP前拒绝错误账号或路径与body编号不符");
        }
        check(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "荧光棒所有拒绝不改变持久状态");
        store.Save(1, original);
    }
}
