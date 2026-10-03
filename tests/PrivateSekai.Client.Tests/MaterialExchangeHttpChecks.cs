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
             {"id":3,"materialExchangeSummaryId":1,"resourceBoxId":9901,"refreshCycle":"weekly"}]
            """);
        File.WriteAllText(Path.Combine(directory, "materialExchangeSummaries.json"),
            """[{"id":1,"materialExchangeType":"normal"}]""");
        var path = Path.Combine(directory, "resourceBoxes.json");
        var boxes = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        boxes.Add(JsonNode.Parse("""
            {"id":9901,"resourceBoxPurpose":"material_exchange",
             "details":[{"resourceType":"practice_ticket","resourceId":1,"resourceQuantity":3}]}
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
        store.Save(1, original);
        await client.Send(new() { Operation = "system" });
    }

    private static ScenarioStep Step(int count, int id = 2) => new()
    {
        Operation = "material-exchange", Args = new() { ["materialExchangeId"] = id.ToString() },
        Query = new() { ["costGroupId"] = "1", ["count"] = count.ToString() }
    };
}
