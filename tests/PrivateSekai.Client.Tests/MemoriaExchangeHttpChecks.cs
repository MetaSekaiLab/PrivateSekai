using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class MemoriaExchangeHttpChecks
{
    public static void WriteMaster(string directory)
    {
        void Add(string table, string value)
        {
            var path = Path.Combine(directory, table + ".json");
            var rows = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsArray() : new JsonArray();
            rows.Add(JsonNode.Parse(value));
            JsonFiles.Write(path, rows);
        }
        Add("materialExchangeSummaries", """{"id":795,"materialExchangeType":"normal"}""");
        Add("materialExchanges", """
            {"id":79501,"materialExchangeSummaryId":795,"resourceBoxId":79501,"refreshCycle":"none",
             "costs":[{"costGroupId":10,"resourceType":"material","resourceId":168,"quantity":1},
              {"costGroupId":20,"resourceType":"mysekai_material","resourceId":79535,"quantity":1},
              {"costGroupId":21,"resourceType":"mysekai_material","resourceId":79536,"quantity":1}],
             "materialExchangeRelationParents":[{"id":7950,"materialExchangeRelationChildren":[
              {"materialExchangeId":79501,"materialExchangeCostGroupId":20},{"materialExchangeId":79501,"materialExchangeCostGroupId":21}]}]}
            """);
        Add("materialExchanges", """
            {"id":79502,"materialExchangeSummaryId":795,"resourceBoxId":79502,"refreshCycle":"none",
             "costs":[{"costGroupId":20,"resourceType":"mysekai_material","resourceId":79535,"quantity":1}]}
            """);
        foreach (var id in new[] { 79535, 79536, 79555 })
            Add("mysekaiMaterials", new JsonObject { ["id"] = id, ["mysekaiMaterialType"] = "game_character" }.ToJsonString());
        Add("resourceBoxes", """
            {"id":79501,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"mysekai_material","resourceId":79555,"resourceQuantity":1}]}
            """);
        Add("resourceBoxes", """
            {"id":79502,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"mysekai_material","resourceId":79555,"resourceQuantity":1},
             {"resourceType":"mysekai_material","resourceId":999998,"resourceQuantity":1}]}
            """);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        void Reset()
        {
            var state = original.DeepClone();
            state.Data.userMaterials = [new() { materialId = 168, quantity = 10 }];
            state.Data.userMysekaiMaterials = [new() { mysekaiMaterialId = 79535, quantity = 10, lastObtainedAt = 80 },
                new() { mysekaiMaterialId = 79536, quantity = 10, lastObtainedAt = 90 }];
            state.Data.userMysekaiMaterialPossession = new() { quantity = 99 };
            state.Data.userMaterialExchanges = [];
            state.Data.refreshableTypes = [];
            store.Save(1, state);
        }
        ScenarioStep Exchange(int group, int count = 1, int id = 79501) => new()
        {
            Operation = "material-exchange", Args = new() { ["materialExchangeId"] = id.ToString() },
            Query = new() { ["costGroupId"] = group.ToString(), ["count"] = count.ToString() }
        };
        Reset();
        await ScenarioRunner.Run(client, new() { Steps = [Exchange(20, 2), Exchange(10, 3), Exchange(21, 5)] }, Path.Combine(directory, "memoria-exchange"));
        var saved = store.Read(1)!.Data;
        check(saved.userMaterials.Single().quantity == 7 && saved.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 79535).quantity == 8
            && saved.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 79536).quantity == 5 && saved.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 79555).quantity == 10,
            "Memoria不同替代组与七色材料经HTTP只扣所选组，奖励共同累计");
        var response = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "memoria-exchange/003.json")))!["response"]!;
        check(response["updatedResources"]!["userMysekaiMaterials"]!.AsArray().Count == 3
            && response["updatedResources"]!["userMysekaiMaterialPossession"] == null, "响应刷新完整Memoria库存，普通仓库占用不变且不重复返回");
        foreach (var (group, count, id, expected) in new[] { (20, 9, 79501, 409), (7950, 1, 79501, 500), (20, 1, 79502, 500) })
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            using var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await rejected.Send(new() { Operation = "system" });
            try { await rejected.Send(Exchange(group, count, id)); } catch (ClientFailure) { }
            check(rejected.LastHttpStatus == expected && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "所选库存不足、父展示ID或后项坏主表经HTTP拒绝并保持全部状态");
        }
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            await fresh.Send(Exchange(20, 8));
            var suite = await fresh.Suite();
            var rows = suite["userMysekaiMaterials"]!.AsArray();
            check(rows.Single(m => m!["mysekaiMaterialId"]!.GetValue<int>() == 79535)!["quantity"]!.GetValue<int>() == 0
                && rows.Single(m => m!["mysekaiMaterialId"]!.GetValue<int>() == 79555)!["quantity"]!.GetValue<int>() == 18,
                "独立认证后继续兑换至源耗尽，Suite回读源零库存与全部奖励");
            check(rows.Single(m => m!["mysekaiMaterialId"]!.GetValue<int>() == 79535)!["lastObtainedAt"]!.GetValue<long>() == 80
                && rows.Single(m => m!["mysekaiMaterialId"]!.GetValue<int>() == 79555)!["lastObtainedAt"]!.GetValue<long>() > 0
                && suite["userMysekaiMaterialPossession"]!["quantity"]!.GetValue<int>() == 99
                && suite["userMaterialExchanges"]!.AsArray().Single()!["totalExchangeCount"]!.GetValue<int>() == 18,
                "独立Suite保留源获得时间、奖励时间、普通占用及总兑换次数");
        }
        store.Save(1, original);
        await client.Send(new() { Operation = "system" });
    }
}
