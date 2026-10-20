using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class CardShopHttpChecks
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
        Add("cards", """{"id":88501,"characterId":8851,"cardRarityType":"rarity_4"}""");
        Add("cards", """{"id":88502,"characterId":8852,"cardRarityType":"rarity_4"}""");
        Add("materialExchangeSummaries", """
            {"id":885,"materialExchangeType":"normal","materialExchangeFreebieGroupJson":{"id":8,"materialExchangeFreebieType":"exchange_already_owned_card"},
             "materialExchangeFreebies":[{"id":1,"materialExchangeFreebieGroupId":8,"materialExchangeFreebieTypeId":8851,"resourceBoxId":8851},
              {"id":2,"materialExchangeFreebieGroupId":8,"materialExchangeFreebieTypeId":8852,"resourceBoxId":8852}]}
            """);
        Add("materialExchangeSummaries", """
            {"id":886,"materialExchangeType":"normal","materialExchangeFreebieGroupJson":{"id":8,"materialExchangeFreebieType":"future"}}
            """);
        foreach (var id in new[] { 88501, 88502, 88503 })
        {
            Add("materialExchanges", new JsonObject { ["id"] = id, ["materialExchangeSummaryId"] = id == 88503 ? 886 : 885,
                ["resourceBoxId"] = id == 88502 ? 88502 : 88501, ["refreshCycle"] = "none",
                ["costs"] = JsonNode.Parse("""[{"costGroupId":1,"resourceType":"material","resourceId":165,"quantity":1}]""") }.ToJsonString());
        }
        Add("resourceBoxes", """{"id":88501,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"card","resourceId":88501,"resourceQuantity":1}]}""");
        Add("resourceBoxes", """{"id":88502,"resourceBoxPurpose":"material_exchange","details":[{"resourceType":"card","resourceId":88502,"resourceQuantity":1}]}""");
        Add("resourceBoxes", """
            {"id":8851,"resourceBoxPurpose":"material_exchange_freebie","resourceBoxType":"expand","details":[
             {"resourceType":"material","resourceId":88560,"resourceQuantity":1},{"resourceType":"material","resourceId":885133,"resourceQuantity":1},
             {"resourceType":"skill_practice_ticket","resourceId":88503,"resourceQuantity":1},{"resourceType":"practice_ticket","resourceId":88503,"resourceQuantity":1}]}
            """);
        Add("resourceBoxes", """{"id":8852,"resourceBoxPurpose":"material_exchange_freebie","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":88561,"resourceQuantity":3}]}""");
        Add("cardExtras", """{"id":885,"cardId":88501,"resourceBoxId":885}""");
        Add("resourceBoxes", """{"id":885,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"custom_profile_collection_resource","resourceId":885821,"resourceQuantity":1},{"resourceType":"stamp","resourceId":885878,"resourceQuantity":1}]}""");
        Add("stamps", """{"id":885878,"characterId1":8851}""");
        Add("cardCostume3ds", """{"cardId":88501,"costume3dId":885011}""");
        Add("costume3ds", """{"id":885011,"characterId":8851,"partType":"body"}""");
        Add("releaseConditions", """{"id":885500,"releaseConditionType":"master_rank","releaseConditionTypeId":88501,"releaseConditionTypeLevel":0}""");
        foreach (var (id, type) in new[] { (885001, "collect_member"), (885002, "waiting_room"), (885003, "collect_costume_3d"), (885004, "collect_stamp") })
            Add("characterMissionV2s", new JsonObject { ["id"] = id, ["characterId"] = 8851, ["characterMissionType"] = type, ["parameterGroupId"] = 8851 }.ToJsonString());
        Add("characterMissionV2ParameterGroups", """{"id":8851,"seq":1,"requirement":1}""");
        Add("honorMissions", """{"id":885100,"honorMissionType":"duplicate_card_total","requirement":1000}""");
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        var state = original.DeepClone();
        state.Data.userCards = [];
        state.Data.userMaterials = [new() { materialId = 165, quantity = 20 }];
        state.Data.userPracticeTickets = [];
        state.Data.userSkillPracticeTickets = [];
        state.Data.userMaterialExchanges = [];
        state.Data.userCustomProfileResources = [];
        state.Data.userStamps = [];
        state.Data.userCostume3dStatuses = [];
        state.Data.userCharacterMissions = [];
        state.Data.userCharacterMissionStatuses = [];
        state.Data.userHonorMissions = [new() { honorMissionType = "duplicate_card_total", progress = 999, achievedMissionIds = [] }];
        state.Data.userMissionStatuses = [];
        state.Data.userReleaseConditions = [];
        state.Data.newReleaseConditions = null;
        state.Data.refreshableTypes = [];
        store.Save(1, state);
        ScenarioStep Exchange(int count = 1, int id = 88501) => new()
        {
            Operation = "material-exchange", Args = new() { ["materialExchangeId"] = id.ToString() },
            Query = new() { ["costGroupId"] = "1", ["count"] = count.ToString() }
        };
        var output = Path.Combine(directory, "card-shop");
        await ScenarioRunner.Run(client, new() { Steps = [Exchange(), Exchange(), Exchange(3)] }, output);
        var first = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!["response"]!["updatedResources"]!;
        var repeat = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "002.json")))!["response"]!["updatedResources"]!;
        check(first["userPracticeTickets"] == null && first["userCards"]!.AsArray().Single()!["duplicateCount"]!.GetValue<int>() == 0
            && first["userCostume3dStatuses"]!.AsArray().Single()!["status"]!.GetValue<string>() == "available", "首次成员兑换经HTTP发卡和初始服装，不发重复赠品");
        check(first["newReleaseConditions"]!.AsArray().Any(c => c!["releaseConditionId"]!.GetValue<int>() == 885500)
            && first["userCharacterMissionV2s"]!.AsArray().SelectMany(m => m!["achievedMissions"]!.AsArray()).Any(s => s!["missionId"]!.GetValue<int>() == 885001),
            "正常兑换响应包含本次解锁和角色收集任务提示");
        check(repeat["userHonorMissions"]!.AsArray().Single(m => m!["honorMissionType"]!.GetValue<string>() == "duplicate_card_total")!["achievedMissionIds"]!.AsArray()
            .Any(id => id!.GetValue<int>() == 885100), "重复成员兑换经HTTP返回本次重复卡称号提示");
        var data = store.Read(1)!.Data;
        check(data.userCards.Single().duplicateCount == 4 && data.userPracticeTickets.Single().quantity == 4 && data.userSkillPracticeTickets.Single().quantity == 4
            && data.userMaterials.Single(m => m.materialId == 88560).quantity == 4 && data.userMaterials.Single(m => m.materialId == 885133).quantity == 4
            && data.userMaterials.Single(m => m.materialId == 165).quantity == 15, "单次、重复和批量成员兑换整箱赠品与费用均按实际张数结算");
        check(data.userCustomProfileResources.Single().quantity == 5 && data.userStamps.Length == 1 && data.newReleaseConditions == null
            && data.userCharacterMissions.All(m => m.achievedMissions.Length == 0) && data.userHonorMissions.All(m => m.achievedMissionIds.Length == 0),
            "卡牌附赠继续累计且所有本次提示不持久化");
        foreach (var (count, id, expected) in new[] { (100, 88501, 409), (1, 88503, 501) })
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            using var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await rejected.Send(new() { Operation = "system" });
            try { await rejected.Send(Exchange(count, id)); } catch (ClientFailure) { }
            check(rejected.LastHttpStatus == expected && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "余额不足和未知赠品类型经HTTP拒绝且全状态不变");
        }
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            var suite = await fresh.Suite();
            check(suite["userCards"]!.AsArray().Single()!["duplicateCount"]!.GetValue<int>() == 4
                && suite["userMaterialExchanges"]!.AsArray().Single()!["totalExchangeCount"]!.GetValue<int>() == 5
                && suite["userPracticeTickets"]!.AsArray().Single()!["quantity"]!.GetValue<int>() == 4
                && suite["userCustomProfileResources"]!.AsArray().Single()!["quantity"]!.GetValue<int>() == 5, "独立认证Suite回读成员、重复数、赠品、附赠和累计兑换次数");
        }
        state = store.Read(1)!;
        state.Data.userPracticeTickets.Single().quantity = int.MaxValue;
        store.Save(1, state);
        using (var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await rejected.Send(new() { Operation = "system" });
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await rejected.Send(Exchange()); } catch (ClientFailure) { }
            check(rejected.LastHttpStatus == 500 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "HTTP赠品最后一项溢出回滚已发资源、卡和费用");
        }
        store.Save(1, original);
        await client.Send(new() { Operation = "system" });
    }
}
