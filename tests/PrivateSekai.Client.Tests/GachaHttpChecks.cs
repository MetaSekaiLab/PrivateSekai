using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class GachaHttpChecks
{
    public static void WriteMaster(string directory)
    {
        // 单卡池保证测试确定性，数值只用于隔离测试。
        File.WriteAllText(Path.Combine(directory, "gachas.json"), """
            [{"id":1,"gachaCeilItemId":9,"gachaDetails":[{"cardId":1,"weight":1}],
              "gachaBehaviors":[{"id":1,"gachaBehaviorType":"normal","spinCount":1,"costResourceType":"jewel","costResourceQuantity":100}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "gachaCeilExchangeSummaries.json"), """
            [{"id":1,"gachaCeilExchanges":[{"id":1,"resourceBoxId":31,"exchangeLimit":2,
              "gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","gachaCeilItemId":9,"quantity":1}}]}]
            """);
        var path = Path.Combine(directory, "resourceBoxes.json");
        var boxes = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        boxes.Add(JsonNode.Parse("""{"id":31,"resourceBoxPurpose":"gacha_ceil_exchange","details":[{"resourceType":"material","resourceId":1,"resourceQuantity":5}]}"""));
        JsonFiles.Write(path, boxes);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
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
            new() { Operation = "gacha-exchange", Body = JsonNode.Parse("""{"gachaCeilExchangeRequest":{"gachaExchangeId":1,"exchangeCount":1}}""")!.AsObject() },
            new() { Operation = "gacha-wish", Body = JsonNode.Parse("""{"gachaId":1,"rateChoiceGachaDetails":[{"rateChoiceGachaWishId":2,"gachaDetailId":3}]}""")!.AsObject() }
        ] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "gacha"));
        var saved = store.Read(1)!.Data;
        check(saved.userCards.Single().duplicateCount == 2 && saved.userGachas.Single().count == 2,
            "抽卡真实 HTTP 发放卡牌并记录两次抽取");
        check(saved.userGachaCeilItems.Single().quantity == 1 && saved.userMaterials.Single().quantity == 5,
            "天井兑换消耗道具并发放资源");
        check(saved.userGachaCeilExchanges.Single().exchangeRemaining == 1 && saved.userRateChoiceGachaWishes.Single().gachaDetailId == 3,
            "兑换剩余次数和愿望选择正确保存");
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "gacha/002.json")))!;
        check(record["query"]!["isPriorityUsePaidJewel"]!.GetValue<string>() == "true" &&
            record["args"]!["gachaId"]!.GetValue<string>() == "1", "脱敏记录保留抽卡路径和付费优先参数");
    }
}
