using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class GachaBonusHttpChecks
{
    public static void WriteMaster(string directory)
    {
        foreach (var (table, row) in new[]
        {
            ("gachas", """{"id":9700,"startAt":1,"gachaBonusId":2,"gachaCeilItemId":9700,"gachaDetails":[{"cardId":9704,"weight":1,"fixedBonusWeight":1}],"gachaPickups":[{"cardId":9704,"gachaPickupType":"normal"}],"gachaCardRarityRates":[{"cardRarityType":"rarity_4","lotteryType":"normal","rate":100}],"gachaBehaviors":[{"id":9700,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300}]}"""),
            ("cards", """{"id":9704,"characterId":1,"cardRarityType":"rarity_4"}"""),
            ("cardEpisodes", """{"id":97040,"cardId":9704}"""),
            ("characterArchiveVoices", """{"id":9704,"groupId":9704,"gameCharacterId":1,"characterArchiveVoiceType":"collect_card","externalId":9704}"""),
            ("releaseConditions", """{"id":9704,"releaseConditionType":"master_rank","releaseConditionTypeId":9704,"releaseConditionTypeLevel":0}"""),
            ("cardCostume3ds", """{"cardId":9704,"costume3dId":97041}"""),
            ("cardCostume3ds", """{"cardId":9704,"costume3dId":97042}"""),
            ("costume3ds", """{"id":97041,"characterId":1,"partType":"head"}"""),
            ("costume3ds", """{"id":97042,"characterId":1,"partType":"body"}""")
        })
        {
            var path = Path.Combine(directory, table + ".json");
            var rows = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsArray() : new JsonArray();
            rows.Add(JsonNode.Parse(row));
            JsonFiles.Write(path, rows);
        }
        JsonFiles.Write(Path.Combine(directory, "gachaBonuses.json"), JsonNode.Parse("""[{"id":2,"switchFixedBonusWeight":true}]""")!);
        JsonFiles.Write(Path.Combine(directory, "clientConfigs.json"), JsonNode.Parse("""[{"id":113,"type":"Float","value":"0.5"},{"id":114,"type":"Float","value":"1.0"}]""")!);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userCards = [];
        state.Data.userChargedCurrency = new() { free = 600 };
        state.Data.userGachaBonusPoints = [new() { userId = 1, gachaId = 9700, gachaBonusPoint = 49, totalGachaBonusPoint = 149 }];
        state.Data.userGachaCeilItems = [];
        state.Data.userGachas = [];
        state.Data.userCostume3dStatuses = [new() { costume3dId = 97041, status = "forbidden" }, new() { costume3dId = 97042, status = "forbidden" }];
        state.Data.userCharacterMissions = [];
        state.Data.userCharacterMissionStatuses = [];
        state.Data.userHonorMissions = [new() { honorMissionType = "collect_costume_3d", progress = 49, achievedMissionIds = [] }];
        state.Data.userMissionStatuses = [];
        state.Data.userBeginnerMissionV2s = [];
        state.Data.refreshableTypes = [];
        store.Save(1, state);
        var step = new ScenarioStep { Operation = "gacha-draw", Args = new() { ["gachaId"] = "9700", ["gachaBehaviorId"] = "9700" } };
        var first = await client.Send(step);
        var response = await client.Send(step);
        var prize = response["obtainPrizes"]!.AsArray().Single()!;
        var refresh = response["updatedResources"]!;
        check(first["obtainPrizes"]!.AsArray().Single()!["gachaLotteryType"]!.GetValue<string>() == "normal" &&
            prize["gachaLotteryType"]!.GetValue<string>() == "fixed_gacha_bonus" && first["obtainPrizes"]![0]!["newFlg"]!.GetValue<bool>() &&
            !prize["newFlg"]!.GetValue<bool>() && first["obtainPrizes"]![0]!["costume3d"]!.AsArray().Count == 2 && prize["costume3d"]!.AsArray().Count == 0,
            "连续两次单抽HTTP各发一张卡，第二次到达50点为保底重复卡，初始服装只发一次");
        check(response["obtainGachaBonusItems"]!.AsArray().Count == 0 &&
            refresh["userGachaBonusPoints"]![0]!["gachaBonusPoint"]!.GetValue<float>() == 50 &&
            refresh["userGachaBonusPoints"]![0]!["totalGachaBonusPoint"]!.GetValue<float>() == 150 &&
            refresh["userGachaBonusPoints"]![0]!["userId"] != null &&
            first["updatedResources"]!["newReleaseConditions"]!.AsArray().Single()!["releaseConditionId"]!.GetValue<int>() == 9704 &&
            first["updatedResources"]!["userHonorMissions"]!.AsArray().Single(m => m!["honorMissionType"]!.GetValue<string>() == "collect_costume_3d")!["achievedMissionIds"]!.AsArray().Single()!.GetValue<int>() == 3101,
            "HTTP返回累计积分、所属用户及本次服装称号达成提示，经典保底不冒充道具奖励");
        using (var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await readback.Send(new() { Operation = "system" });
            var suite = await readback.Suite();
            check(suite["userGachaBonusPoints"]![0]!["gachaBonusPoint"]!.GetValue<float>() == 50 &&
                suite["userCards"]![0]!["duplicateCount"]!.GetValue<int>() == 1 &&
                suite["userCostume3dStatuses"]!.AsArray().Count(c => c!["status"]!.GetValue<string>() == "available") == 2 &&
                suite["userHonorMissions"]!.AsArray().Single(m => m!["honorMissionType"]!.GetValue<string>() == "collect_costume_3d")!["achievedMissionIds"]!.AsArray().Count == 0,
                "独立会话保留积分、保底重复卡与服装，清除本次称号提示");
        }
        state = store.Read(1)!;
        state.Data.refreshableTypes = ["userMaterials"];
        store.Save(1, state);
        var before = DumpSerializer.Serialize(state.Data);
        using (var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await invalid.Send(new() { Operation = "system" });
            var failed = false;
            try { await invalid.Send(step); } catch (ClientFailure) { failed = true; }
            check(failed && invalid.LastHttpStatus == 409 && invalid.LastResponse?["errorCode"]?.GetValue<string>() == "not_enough_resource" &&
                DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "HTTP余额不足保留积分、奖品、任务及待刷新字段");
        }
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }
}
