using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class MaterialExchangeHttpChecks
{
    public static void WriteMaster(string directory)
    {
        // 隔离测试数据，不代表官方成本。
        File.WriteAllText(Path.Combine(directory, "materialExchanges.json"), """
            [{"id":2,"materialExchangeSummaryId":1,"resourceBoxId":9901,"refreshCycle":"none",
              "costs":[{"costGroupId":1,"resourceType":"material","resourceId":1,"quantity":2}]},
             {"id":3,"materialExchangeSummaryId":1,"resourceBoxId":9901,"refreshCycle":"weekly"},
             {"id":44,"materialExchangeSummaryId":1,"resourceBoxId":9902,"refreshCycle":"monthly","exchangeLimit":15,
              "costs":[{"costGroupId":1,"resourceType":"material","resourceId":15,"quantity":300}]},
             {"id":45,"materialExchangeSummaryId":1,"resourceBoxId":9903,"refreshCycle":"monthly","exchangeLimit":15,
              "costs":[{"costGroupId":1,"resourceType":"material","resourceId":15,"quantity":100}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "materialExchangeSummaries.json"),
            """[{"id":1,"materialExchangeType":"normal"}]""");
        var path = Path.Combine(directory, "resourceBoxes.json");
        var boxes = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        boxes.Add(JsonNode.Parse("""
            {"id":9901,"resourceBoxPurpose":"material_exchange",
             "details":[{"resourceType":"practice_ticket","resourceId":1,"resourceQuantity":3}]}
            """));
        boxes.Add(JsonNode.Parse("""
            {"id":9902,"resourceBoxPurpose":"material_exchange",
             "details":[{"resourceType":"material","resourceId":57,"resourceQuantity":1}]}
            """));
        boxes.Add(JsonNode.Parse("""
            {"id":9903,"resourceBoxPurpose":"material_exchange",
             "details":[{"resourceType":"virtual_coin","resourceQuantity":300}]}
            """));
        JsonFiles.Write(path, boxes);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = original.DeepClone();
        state.Data.userMaterials = [new() { materialId = 1, quantity = 10 }];
        state.Data.userPracticeTickets = [];
        state.Data.userMaterialExchanges = [];
        store.Save(1, state);
        await ScenarioRunner.Run(client, new() { Steps = [Step(2), Step(1)] }, Path.Combine(directory, "material-exchange"));
        var saved = store.Read(1)!.Data;
        var exchange = saved.userMaterialExchanges.Single();
        check(saved.userMaterials.Single().quantity == 4 && saved.userPracticeTickets.Single().quantity == 9 &&
            exchange.exchangeCount == 3 && exchange.totalExchangeCount == 3 && exchange.lastExchangedAt > 0,
            "材料兑换按 master 倍乘扣发并累计两次请求");
        var response = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "material-exchange/002.json")))!["response"]!;
        check(response["releasedActionSetIds"]!.AsArray().Count == 0 &&
            response["updatedResources"]!["userMaterialExchanges"]![0]!["exchangeRemaining"] == null &&
            response["updatedResources"]!["userMaterialExchanges"]![0]!["refreshedAt"] == null,
            "无上限无刷新兑换省略未发生字段");
        foreach (var (count, id, expected) in new[] { (3, 2, 409), (1, 3, 501) })
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            using var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await rejected.Send(new() { Operation = "system" });
            var failed = false;
            try { await rejected.Send(Step(count, id)); }
            catch (ClientFailure) { failed = true; }
            check(failed && rejected.LastHttpStatus == expected &&
                before.SequenceEqual(DumpSerializer.Serialize(store.Read(1)!.Data)),
                "材料不足或未支持周期兑换不改变用户状态");
        }
        state = original.DeepClone();
        state.Data.userMaterials = [new() { materialId = 15, quantity = 10000 }];
        state.Data.userGamedata.virtualCoin = 0;
        state.Data.userMaterialExchanges = [];
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [Step(2, 44), Step(3, 45), Step(13, 44)] }, Path.Combine(directory, "monthly-material-exchange"));
        saved = store.Read(1)!.Data;
        check(saved.userMaterials.Single(m => m.materialId == 15).quantity == 5200 && saved.userMaterials.Single(m => m.materialId == 57).quantity == 15
            && saved.userGamedata.virtualCoin == 900 && saved.userMaterialExchanges.Single(e => e.materialExchangeId == 44).exchangeStatus == "not_exchangeable",
            "月限材料和虚拟币经加密HTTP批量扣发，最后次数售罄");
        using (var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await rejected.Send(new() { Operation = "system" });
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await rejected.Send(Step(1, 44)); } catch (ClientFailure) { }
            check(rejected.LastHttpStatus == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "HTTP超月限拒绝且全部状态不变");
        }
        state = store.Read(1)!;
        var last = state.Data.userMaterialExchanges.Single(e => e.materialExchangeId == 44).lastExchangedAt;
        foreach (var item in state.Data.userMaterialExchanges) item.refreshedAt = 1;
        store.Save(1, state);
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            var suite = await fresh.Suite();
            var rows = suite["userMaterialExchanges"]!.AsArray();
            var monthly = rows.Single(e => e!["materialExchangeId"]!.GetValue<int>() == 44)!;
            var now = DateTimeOffset.FromUnixTimeMilliseconds(suite["now"]!.GetValue<long>()).ToOffset(TimeSpan.FromHours(9));
            var next = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddMonths(1).ToUnixTimeMilliseconds();
            check(rows.All(e => e!["exchangeCount"]!.GetValue<int>() == 0 && e["exchangeRemaining"]!.GetValue<int>() == 15
                    && e["exchangeStatus"]!.GetValue<string>() == "exchangeable" && e["refreshedAt"]!.GetValue<long>() == next)
                && monthly["totalExchangeCount"]!.GetValue<int>() == 15 && monthly["lastExchangedAt"]!.GetValue<long>() == last,
                "独立认证Suite恢复已过月限，保留累计和最后兑换时间，保存下月日本零点");
            check(suite["userMaterials"]!.AsArray().Single(m => m!["materialId"]!.GetValue<int>() == 57)!["quantity"]!.GetValue<int>() == 15
                && suite["userGamedata"]!["virtualCoin"]!.GetValue<int>() == 900, "月度刷新不重复发放奖励");
            await fresh.Send(Step(15, 45));
            suite = await fresh.Suite();
            check(suite["userGamedata"]!["virtualCoin"]!.GetValue<int>() == 5400
                && suite["userMaterialExchanges"]!.AsArray().Single(e => e!["materialExchangeId"]!.GetValue<int>() == 45)!["totalExchangeCount"]!.GetValue<int>() == 18,
                "月切后从正常兑换入口再次用完整限额，虚拟币和历史累计继续增加");
        }
        store.Save(1, original);
        await client.Send(new() { Operation = "system" });
    }

    private static ScenarioStep Step(int count, int id = 2) => new()
    {
        Operation = "material-exchange", Args = new() { ["materialExchangeId"] = id.ToString() },
        Query = new() { ["costGroupId"] = "1", ["count"] = count.ToString() }
    };
}
