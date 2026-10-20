using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class GachaExtraHttpChecks
{
    public static void WriteMaster(string directory)
    {
        foreach (var (table, row) in new[]
        {
            ("gachas", """
                {"id":9850,"gachaType":"normal","startAt":1,"gachaDetails":[{"cardId":9854,"weight":1}],
                 "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100}],
                 "gachaBehaviors":[{"id":9850,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":9850},
                  {"id":9851,"spinCount":10,"costResourceType":"jewel","costResourceQuantity":3000,"gachaExtraId":9851},
                  {"id":9852,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":9852},
                  {"id":9853,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":9853},
                  {"id":9854,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":999999}]}
                """),
            ("cards", """{"id":9854,"characterId":1,"cardRarityType":"rarity_2"}"""),
            ("gachaExtras", """{"id":9850,"resourceBoxId":9850}"""),
            ("gachaExtras", """{"id":9851,"resourceBoxId":9851}"""),
            ("gachaExtras", """{"id":9852,"resourceBoxId":9852}"""),
            ("gachaExtras", """{"id":9853,"resourceBoxId":9853}"""),
            ("resourceBoxes", """{"id":9850,"resourceBoxPurpose":"gacha_extra","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":98,"resourceQuantity":3}]}"""),
            ("resourceBoxes", """{"id":9851,"resourceBoxPurpose":"gacha_extra","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":98,"resourceQuantity":30}]}"""),
            ("resourceBoxes", """
                {"id":9852,"resourceBoxPurpose":"gacha_extra","resourceBoxType":"expand","details":[
                 {"resourceType":"material","resourceId":98,"resourceQuantity":5},
                 {"resourceType":"costume_3d","resourceId":98541,"resourceQuantity":1},
                 {"resourceType":"costume_3d","resourceId":98542,"resourceQuantity":1},
                 {"resourceType":"costume_3d","resourceId":98543,"resourceQuantity":1},
                 {"resourceType":"custom_profile_collection_resource","resourceId":9850,"resourceQuantity":2}]}
                """),
            ("resourceBoxes", """
                {"id":9853,"resourceBoxPurpose":"gacha_extra","resourceBoxType":"costume_3d","details":[
                 {"resourceType":"costume_3d","resourceId":98541,"resourceQuantity":1},
                 {"resourceType":"costume_3d","resourceId":98542,"resourceQuantity":1},
                 {"resourceType":"costume_3d","resourceId":98543,"resourceQuantity":1}]}
                """),
            ("costume3ds", """{"id":98541,"characterId":1,"partType":"head"}"""),
            ("costume3ds", """{"id":98542,"characterId":1,"partType":"body"}"""),
            ("costume3ds", """{"id":98543,"characterId":1,"partType":"hair"}""")
        })
        {
            var path = Path.Combine(directory, table + ".json");
            var rows = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsArray() : new JsonArray();
            rows.Add(JsonNode.Parse(row));
            JsonFiles.Write(path, rows);
        }
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        void Reset()
        {
            var state = original.DeepClone();
            state.Data.userChargedCurrency = new() { free = 6000 };
            state.Data.userCards = [];
            state.Data.userMaterials = [];
            state.Data.userGachas = [];
            state.Data.userCostume3dStatuses = [new() { costume3dId = 98541, status = "available", obtainedAt = 123 }];
            state.Data.userCustomProfileResources = [new() { customProfileResourceType = "material", customProfileResourceId = 9850, quantity = 19 }];
            state.Data.userCharacterMissions = [];
            state.Data.userCharacterMissionStatuses = [];
            state.Data.userHonorMissions = [new() { honorMissionType = "collect_costume_3d", progress = 49, achievedMissionIds = [] }];
            state.Data.refreshableTypes = [];
            store.Save(1, state);
        }
        ScenarioStep Draw(int behavior) => new() { Operation = "gacha-draw", Args = new()
            { ["gachaId"] = "9850", ["gachaBehaviorId"] = behavior.ToString() } };
        Reset();
        var first = await client.Send(Draw(9852));
        var second = await client.Send(Draw(9852));
        var refresh = second["updatedResources"]!;
        check(first["obtainPrizes"]![0]!["newFlg"]!.GetValue<bool>() && !second["obtainPrizes"]![0]!["newFlg"]!.GetValue<bool>()
            && second["obtainGachaExtras"]!.AsArray().Count == 0 && second["obtainGachaFreebies"]!.AsArray().Count == 0,
            "固定附赠不占卡牌名额，也不混入gift或随机freebie结果数组");
        check(refresh["userMaterials"]!.AsArray().Single(m => m!["materialId"]!.GetValue<int>() == 98)!["quantity"]!.GetValue<int>() == 10
            && refresh["userCustomProfileResources"]!.AsArray().Single(r => r!["customProfileResourceType"]!.GetValue<string>() == "collection")!["quantity"]!.GetValue<int>() == 4,
            "两次抽卡附赠材料和名片收藏通过实际加密HTTP刷新，数量正确累计");
        var stored = store.Read(1)!.Data;
        check(stored.userCostume3dStatuses.Count(c => c.status == "available") == 3
            && stored.userCostume3dStatuses.Single(c => c.costume3dId == 98541).obtainedAt == 123
            && stored.userCharacterMissions.Single(m => m.characterMissionType == "collect_costume_3d").progress == 1
            && stored.userHonorMissions.Single(m => m.honorMissionType == "collect_costume_3d").progress == 50,
            "首次附赠身体服装推进一次收集，重复抽取不重记套数或覆盖原有服装时间");
        check(first["updatedResources"]!["userHonorMissions"]!.AsArray().Single(m => m!["honorMissionType"]!.GetValue<string>() == "collect_costume_3d")!["achievedMissionIds"]!.AsArray().Count == 1,
            "附赠服装达到称号门槛时仍通过已有响应映射返回本次提示");
        using (var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await readback.Send(new() { Operation = "system" });
            var suite = await readback.Suite();
            check(suite["userCustomProfileResources"]!.AsArray().Single(r => r!["customProfileResourceType"]!.GetValue<string>() == "collection")!["quantity"]!.GetValue<int>() == 4
                && suite["userCostume3dStatuses"]!.AsArray().Count(c => c!["status"]!.GetValue<string>() == "available") == 3
                && suite["userHonorMissions"]!.AsArray().Single(m => m!["honorMissionType"]!.GetValue<string>() == "collect_costume_3d")!["achievedMissionIds"]!.AsArray().Count == 0,
                "独立Suite保留附赠库存和套装，称号临时提示不持久化");
        }
        await client.Send(new() { Operation = "system" });
        Reset();
        var twice = Draw(9850);
        twice.Query["executeCount"] = "2";
        await ScenarioRunner.Run(client, new() { Steps = [twice, Draw(9851)] }, Path.Combine(directory, "gacha-extras"));
        check(store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 98).quantity == 36
            && store.Read(1)!.Data.userChargedCurrency.free == 2400
            && store.Read(1)!.Data.userGachas.Single(g => g.gachaBehaviorId == 9850).count == 2
            && store.Read(1)!.Data.userGachas.Single(g => g.gachaBehaviorId == 9851).count == 1,
            "现有Client场景完成指定双单抽和一次十连，附赠6加30、费用和次数贯通");
        Reset();
        await client.Send(Draw(9853));
        check(store.Read(1)!.Data.userCostume3dStatuses.Count(c => c.status == "available") == 3,
            "costume_3d箱发放全部配置服装，不把客户端压缩展示的代表服装当成奖励全集");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        using (var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await invalid.Send(new() { Operation = "system" });
            var failed = false;
            try { await invalid.Send(Draw(9854)); }
            catch (ClientFailure) { failed = true; }
            check(failed && invalid.LastHttpStatus == 500 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "附赠配置缺失实际HTTP失败，费用、卡牌、服装及全部用户状态回滚");
        }
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }
}
