using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class GachaHttpChecks
{
    public static void WriteMaster(string directory)
    {
        File.WriteAllText(Path.Combine(directory, "cardExtras.json"), "[]");
        // 单卡池保证测试确定性，数值只用于隔离测试。
        File.WriteAllText(Path.Combine(directory, "gachas.json"), """
            [{"id":1,"startAt":1,"gachaCeilItemId":9,"gachaDetails":[{"cardId":1,"weight":1}],
              "gachaCardRarityRates":[{"cardRarityType":"rarity_1","lotteryType":"normal","rate":100}],
              "gachaBehaviors":[{"id":1,"gachaBehaviorType":"normal","spinCount":1,"costResourceType":"jewel","costResourceQuantity":100},
                {"id":2,"gachaBehaviorType":"normal","spinCount":1,"costResourceType":"gacha_ticket","costResourceId":1,"costResourceQuantity":1},
                {"id":3,"gachaBehaviorType":"normal","spinCount":1,"costResourceType":"jewel","costResourceQuantity":1000}]},
             {"id":2,"startAt":1,"endAt":2,"gachaBehaviors":[{"id":4,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":100}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "gachaCeilExchangeSummaries.json"), """
            [{"id":1,"startAt":1,"gachaCeilExchanges":[{"id":1,"startAt":1,"resourceBoxId":31,"exchangeLimit":2,
              "gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","gachaCeilItemId":9,"quantity":1}}]},
             {"id":2,"startAt":1,"endAt":2,"gachaCeilExchanges":[{"id":2,"startAt":1,"endAt":2,"resourceBoxId":31,
              "gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","gachaCeilItemId":9,"quantity":1}}]}]
            """);
        var path = Path.Combine(directory, "resourceBoxes.json");
        var boxes = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        boxes.Add(JsonNode.Parse("""{"id":31,"resourceBoxPurpose":"gacha_ceil_exchange","details":[{"resourceType":"material","resourceId":1,"resourceQuantity":5}]}"""));
        JsonFiles.Write(path, boxes);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var query = new ScenarioStep { Operation = "gacha-draw", Args = new() { ["gachaId"] = "1", ["gachaBehaviorId"] = "1" },
            Query = new() { ["isPriorityUsePaidJewel"] = "True", ["executeCount"] = "2" } };
        check(Operations.Path(Operations.All["gacha-draw"], query, 1) ==
            "/api/user/1/gacha/1/gachaBehaviorId/1?isPriorityUsePaidJewel=true&executeCount=2", "指定抽数沿用同一路由，正整数查询值与布尔值规范编码");
        foreach (var count in new[] { "0", "-1", "1.5", "2147483648" })
        {
            query.Query["executeCount"] = count;
            var rejected = false;
            try { Operations.Path(Operations.All["gacha-draw"], query, 1); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "非法指定抽数在客户端发送前拒绝");
        }
        var state = store.Read(1)!;
        state.Data.userCards = [new() { cardId = 1, level = 1 }];
        state.Data.userChargedCurrency = new() { paid = 400, free = 400 };
        state.Data.userMaterials = [];
        store.Save(1, state);
        var scenario = new Scenario { Steps =
        [
            new() { Operation = "gacha-draw", Args = new() { ["gachaId"] = "1", ["gachaBehaviorId"] = "1" },
                Expect = new() { ["/updatedResources/userChargedCurrency/free"] = JsonValue.Create(300),
                    ["/updatedResources/userChargedCurrency/paid"] = JsonValue.Create(400) } },
            new() { Operation = "gacha-draw", Args = new() { ["gachaId"] = "1", ["gachaBehaviorId"] = "1" },
                Query = new() { ["isPriorityUsePaidJewel"] = "true" },
                Expect = new() { ["/updatedResources/userChargedCurrency/free"] = JsonValue.Create(300),
                    ["/updatedResources/userChargedCurrency/paid"] = JsonValue.Create(300) } },
            new() { Operation = "gacha-exchange", Body = JsonNode.Parse("""{"gachaCeilExchangeRequest":{"gachaExchangeId":1,"exchangeCount":1}}""")!.AsObject() }
        ] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "gacha"));
        var saved = store.Read(1)!.Data;
        check(saved.userCards.Single().duplicateCount == 2 && saved.userGachas.Single().count == 2,
            "抽卡真实 HTTP 发放卡牌并记录两次抽取");
        check(saved.userGachaCeilItems.Single().quantity == 1 && saved.userMaterials.Single().quantity == 5,
            "天井兑换消耗道具并发放资源");
        check(saved.userGachaCeilExchanges.Single().exchangeRemaining == 1, "兑换剩余次数正确保存");
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "gacha/002.json")))!;
        check(record["query"]!["isPriorityUsePaidJewel"]!.GetValue<string>() == "true" &&
            record["args"]!["gachaId"]!.GetValue<string>() == "1", "脱敏记录保留抽卡路径和付费优先参数");
        check(record["response"]!["consumedCosts"]![0]!["quantity"]!.GetValue<int>() == 600 &&
            record["response"]!["consumedCosts"]![0]!["resourceId"] == null &&
            record["response"]!["updatedResources"]!["userGachaCeilItems"]![0]!["userId"] == null,
            "单抽消耗响应为剩余余额，天井道具省略所属用户字段");
        var original = store.Read(1)!;
        state = store.Read(1)!;
        state.Data.refreshableTypes = ["userMaterials"];
        store.Save(1, state);
        var before = DumpSerializer.Serialize(state.Data);
        foreach (var entry in new[] { (Id: 0, Behavior: 0, Status: 404, Code: ""),
            (Id: 1, Behavior: int.MaxValue, Status: 404, Code: ""), (Id: 2, Behavior: 4, Status: 409, Code: "invalid_term"),
            (Id: 1, Behavior: 2, Status: 409, Code: "not_enough_resource"), (Id: 1, Behavior: 3, Status: 409, Code: "not_enough_resource") })
        {
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var failed = false;
            try { await invalid.Send(new() { Operation = "gacha-draw", Args = new() { ["gachaId"] = entry.Id.ToString(), ["gachaBehaviorId"] = entry.Behavior.ToString() } }); }
            catch (ClientFailure) { failed = true; }
            check(failed && invalid.LastHttpStatus == entry.Status && invalid.LastResponse?["errorCode"]?.GetValue<string>() == entry.Code &&
                invalid.LastResponse?["errorMessage"]?.GetValue<string>() == "", "抽卡加密HTTP返回官方错误分类与空文案");
            check(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "抽卡HTTP拒绝整体回滚并保留待刷新字段");
        }
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }

    public static async Task RunExchange(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userGachaCeilItems = [new() { gachaCeilItemId = 9, quantity = 3 }];
        state.Data.userGachaCeilExchanges = [];
        state.Data.userMaterials = [];
        state.Data.refreshableTypes = [];
        store.Save(1, state);
        ScenarioStep Exchange(int id, int count) => new() { Operation = "gacha-exchange", Body = new()
        {
            ["gachaCeilExchangeRequest"] = new JsonObject { ["gachaExchangeId"] = id, ["exchangeCount"] = count }
        } };
        var response = await client.Send(Exchange(1, 2));
        check(response["obtainUserResources"]![0]!["quantity"]!.GetValue<int>() == 10 &&
            response["obtainUserResources"]![0]!["resourceLevel"] == null &&
            response["updatedResources"]!["userGachaCeilExchanges"]![0]!["exchangeRemaining"]!.GetValue<int>() == 0,
            "批量天井兑换返回总获得量、省略零等级，售罄响应保留零剩余次数");
        using (var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await readback.Send(new() { Operation = "system" });
            var suite = await readback.Suite();
            check(suite["userGachaCeilItems"]![0]!["quantity"]!.GetValue<int>() == 1 &&
                suite["userMaterials"]![0]!["quantity"]!.GetValue<int>() == 10 &&
                suite["userGachaCeilExchanges"]![0]!["exchangeStatus"]!.GetValue<string>() == "not_exchangeable",
                "独立会话回读同时保存天井扣除、材料到账和售罄状态");
        }
        state = store.Read(1)!;
        state.Data.refreshableTypes = ["userMaterials"];
        store.Save(1, state);
        var before = DumpSerializer.Serialize(state.Data);
        foreach (var entry in new[]
        {
            (Step: Exchange(1, 1), Status: 409, Code: ""), (Step: Exchange(0, 1), Status: 404, Code: ""),
            (Step: Exchange(1, 0), Status: 400, Code: ""), (Step: Exchange(1, -1), Status: 400, Code: ""),
            (Step: Exchange(2, 1), Status: 409, Code: "invalid_term"),
            (Step: new ScenarioStep { Operation = "gacha-exchange", Body = new() }, Status: 500, Code: "")
        })
        {
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var failed = false;
            try { await invalid.Send(entry.Step); } catch (ClientFailure) { failed = true; }
            check(failed && invalid.LastHttpStatus == entry.Status && (entry.Status == 500 ?
                invalid.LastResponse?["byteCount"]?.GetValue<int>() == 0 :
                invalid.LastResponse?["errorCode"]?.GetValue<string>() == entry.Code && invalid.LastResponse?["errorMessage"]?.GetValue<string>() == ""),
                "天井兑换HTTP按官方样本返回非法数量、未知、过期、售罄与缺失详情错误");
            check(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "兑换HTTP拒绝保留完整库存、剩余次数及待刷新字段");
        }
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }
}
