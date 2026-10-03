using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class CardHttpChecks
{
    public static void WriteMaster(string directory)
    {
        // 仅用于隔离测试，不代表官方数值。
        var tables = new Dictionary<string, string>
        {
            ["cards"] = """[{"id":1,"characterId":1,"cardRarityType":"rarity_1"}]""",
            ["cardRarities"] = """[{"cardRarityType":"rarity_1","maxLevel":3,"trainingMaxLevel":3}]""",
            ["practiceTickets"] = """[{"id":1,"exp":100}]""",
            ["levels"] = """[{"levelType":"card","level":1,"totalExp":0},{"levelType":"card","level":2,"totalExp":100},{"levelType":"card","level":3,"totalExp":300}]""",
            ["beginnerMissionV2s"] = """[{"id":6,"beginnerMissionV2Type":"any_card_level_up","requirement":1}]""",
            ["masterLessons"] = """[{"id":1,"costs":[{"id":11,"resourceType":"material","resourceId":1,"quantity":2}]}]""",
            ["masterLessonRewards"] = "[]",
            ["cardExchangeResources"] = """[{"cardRarityType":"rarity_1","seq":1,"resourceBoxId":12}]""",
            ["resourceBoxes"] = """[{"id":12,"resourceBoxPurpose":"card_exchange_resource","details":[{"resourceType":"material","resourceId":1,"resourceQuantity":3}]}]"""
        };
        foreach (var (table, json) in tables) File.WriteAllText(Path.Combine(directory, table + ".json"), json);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userCards = [new() { cardId = 1, level = 1, duplicateCount = 2, defaultImage = "special_training" }];
        state.Data.userCards[0].episodes = [new()
        {
            cardEpisodeId = 52, scenarioStatus = "can_not_read",
            scenarioStatusReasons = ["unread_before_scenario", "not_enough_release_condition"]
        }];
        state.Data.userPracticeTickets = [new() { practiceTicketId = 1, quantity = 2 }];
        state.Data.userMaterials = [new() { materialId = 1, quantity = 10 }];
        store.Save(1, state);
        var steps = new[]
        {
            Step("card-practice", """{"costs":[{"resourceType":"practice_ticket","resourceId":1,"quantity":1}]}"""),
            Step("card-master-lesson", """{"masterLessonCostIds":[11]}"""),
            Step("card-default-image", """{"defaultImage":"original"}"""),
            Step("card-exchange", """{"userCards":[{"cardId":1,"duplicateCount":1}]}""")
        };
        var scenario = new Scenario { Steps = [.. steps] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "cards"));
        var saved = store.Read(1)!.Data;
        var card = saved.userCards.Single();
        check(card.level == 2 && card.totalExp == 100 && saved.userPracticeTickets.Single().quantity == 1,
            "练习客户端真实 HTTP 扣券、增加经验并升级");
        check(card.masterRank == 1 && saved.userMaterials.Single().quantity == 11,
            "突破消耗与等待室转换奖励共同生效，readonly 请求字段正确编码");
        check(card.defaultImage == "original" && card.duplicateCount == 1, "卡面切换与重复卡转换路由正确");
        check(card.episodes.Single().scenarioStatusReasons.Length == 2,
            "未达到等级门槛时保留剧情阻挡原因");
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "cards/001.json")))!;
        check(record["response"]!["updateExpResult"]!["afterLevel"]!.GetValue<int>() == 2 &&
            record["stateChanges"]!.AsArray().Count > 0, "练习响应与状态差异记录完整");
        check(record["response"]!["updatedResources"]!["userBeginnerMissionV2s"]!.AsArray()
            .Single(m => m!["beginnerMissionV2Id"]!.GetValue<int>() == 6)!["isNewAchieved"]!.GetValue<bool>() &&
            !saved.userBeginnerMissionV2s.Single(m => m.beginnerMissionV2Id == 6).isNewAchieved,
            "首次练习达成标志仅存在于响应");

        state = store.Read(1)!;
        state.Data.userCards.Single().level = 1;
        state.Data.userCards.Single().totalExp = 0;
        state.Data.userCards.Single().exp = 0;
        state.Data.userPracticeTickets.Single().quantity = 5;
        store.Save(1, state);
        await ScenarioRunner.Run(client, new() { Steps = [Step("card-practice",
            """{"costs":[{"resourceType":"practice_ticket","resourceId":1,"quantity":4}]}""")] },
            Path.Combine(directory, "card-practice-cap"));
        saved = store.Read(1)!.Data;
        check(saved.userCards.Single().level == 3 && saved.userCards.Single().totalExp == 300 &&
            saved.userCards.Single().exp == 0 && saved.userPracticeTickets.Single().quantity == 1,
            "练习溢出截断到满级并消耗全部提交练习券");
        check(saved.userBeginnerMissionV2s.Single(m => m.beginnerMissionV2Id == 6).progress == 3,
            "已达成练习任务继续累计本次提升的两级");
        check(saved.userCards.Single().episodes.Single().scenarioStatus == "can_not_read" &&
            saved.userCards.Single().episodes.Single().scenarioStatusReasons.SequenceEqual(["unread_before_scenario"]),
            "达到等级后仍保留前篇未读阻挡");
        var repeated = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "card-practice-cap/001.json")))!;
        check(!repeated["response"]!["updatedResources"]!["userBeginnerMissionV2s"]!.AsArray()
            .Single(m => m!["beginnerMissionV2Id"]!.GetValue<int>() == 6)!["isNewAchieved"]!.GetValue<bool>(),
            "后续练习不重复报告首次达成");
        state = store.Read(1)!;
        state.Data.userCards.Single().level = 2;
        state.Data.userCards.Single().totalExp = 100;
        state.Data.userCards.Single().episodes.Single().scenarioStatusReasons = ["not_enough_release_condition"];
        state.Data.userPracticeTickets.Single().quantity = 2;
        store.Save(1, state);
        await ScenarioRunner.Run(client, new() { Steps = [Step("card-practice",
            """{"costs":[{"resourceType":"practice_ticket","resourceId":1,"quantity":2}]}""")] },
            Path.Combine(directory, "card-practice-story"));
        var episode = store.Read(1)!.Data.userCards.Single().episodes.Single();
        check(episode.scenarioStatus == "unreleased" && episode.scenarioStatusReasons.Length == 0,
            "前篇已读且等级达标后，剧情变为可解锁");
        var original = store.Read(1)!;
        state = original.DeepClone();
        state.Data.userCards.Single().specialTrainingStatus = "done";
        store.Save(1, state);
        var before = PrivateSekai.Protocol.DumpSerializer.Serialize(state.Data);
        using var repeatClient = new ProtocolClient(config, directory,
            PrivateSekai.Config.ServerConfig.AesKey.ToArray(), PrivateSekai.Config.ServerConfig.AesIv.ToArray());
        await repeatClient.Send(new() { Operation = "system" });
        var rejected = false;
        try { await repeatClient.Send(Step("special-training", """{"specialTrainingStatus":"done"}""")); }
        catch (ClientFailure) { rejected = true; }
        check(rejected && repeatClient.LastHttpStatus == 409 &&
            before.SequenceEqual(PrivateSekai.Protocol.DumpSerializer.Serialize(store.Read(1)!.Data)),
            "重复特训返回 409，完整用户数据保持不变");
        store.Save(1, original);
        await client.Send(new() { Operation = "system" });
    }

    private static ScenarioStep Step(string operation, string body) => new()
    {
        Operation = operation, Body = JsonNode.Parse(body)!.AsObject(),
        Args = operation == "card-exchange" ? [] : new() { ["cardId"] = "1" }
    };
}
