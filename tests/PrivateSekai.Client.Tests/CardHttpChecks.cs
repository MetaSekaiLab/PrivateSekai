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
            ["cards"] = """[{"id":1,"cardRarityType":"rarity_1"}]""",
            ["cardRarities"] = """[{"cardRarityType":"rarity_1","maxLevel":3,"trainingMaxLevel":3}]""",
            ["practiceTickets"] = """[{"id":1,"exp":100}]""",
            ["levels"] = """[{"levelType":"card","level":1,"totalExp":0},{"levelType":"card","level":2,"totalExp":100},{"levelType":"card","level":3,"totalExp":300}]""",
            ["beginnerMissionV2s"] = """[{"id":6,"requirement":1}]""",
            ["masterLessons"] = """[{"id":1,"costs":[{"id":11,"resourceType":"material","resourceId":1,"quantity":2}]}]""",
            ["masterLessonRewards"] = "[]",
            ["cardExchangeResources"] = """[{"cardRarityType":"rarity_1","seq":1,"resourceBoxId":12}]""",
            ["resourceBoxes"] = """[{"id":12,"resourceBoxPurpose":"card_exchange_resource","details":[{"resourceType":"material","resourceId":1,"resourceQuantity":3}]}]"""
        };
        foreach (var (table, json) in tables) File.WriteAllText(Path.Combine(directory, table + ".json"), json);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userCards = [new() { cardId = 1, level = 1, duplicateCount = 2, defaultImage = "special_training" }];
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
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "cards/001.json")))!;
        check(record["response"]!["updateExpResult"]!["afterLevel"]!.GetValue<int>() == 2 &&
            record["stateChanges"]!.AsArray().Count > 0, "练习响应与状态差异记录完整");
    }

    private static ScenarioStep Step(string operation, string body) => new()
    {
        Operation = operation, Body = JsonNode.Parse(body)!.AsObject(),
        Args = operation == "card-exchange" ? [] : new() { ["cardId"] = "1" }
    };
}
