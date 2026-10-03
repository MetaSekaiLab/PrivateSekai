using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class EventExchangeHttpChecks
{
    public static void WriteMaster(string directory)
    {
        // 隔离夹具，不代表官方编号和数值。
        File.WriteAllText(Path.Combine(directory, "eventExchangeSummaries.json"), """
            [{"id":1,"eventId":1,"eventExchanges":[{"id":1,"eventExchangeSummaryId":1,"exchangeLimit":8,
              "resourceBoxId":9902,"eventExchangeCost":{"resourceType":"event_item","resourceId":1,"resourceQuantity":2}},
             {"id":2,"eventExchangeSummaryId":1,"exchangeLimit":2,"resourceBoxId":9903,
              "eventExchangeCost":{"resourceType":"event_item","resourceId":1,"resourceQuantity":1}},
             {"id":3,"eventExchangeSummaryId":1,"resourceBoxId":9904,
              "eventExchangeCost":{"resourceType":"event_item","resourceId":1,"resourceQuantity":1}}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "eventMissions.json"), """
            [{"id":1,"eventId":1,"eventMissionType":"consume_event_item","eventMissionCategory":"normal","requirement1":20}]
            """);
        var path = Path.Combine(directory, "resourceBoxes.json");
        var boxes = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        boxes.Add(JsonNode.Parse("""
            {"id":9902,"resourceBoxPurpose":"event_exchange",
             "details":[{"resourceType":"skill_practice_ticket","resourceId":2,"resourceQuantity":1}]}
            """));
        boxes.Add(JsonNode.Parse("""
            {"id":9903,"resourceBoxPurpose":"event_exchange",
             "details":[{"resourceType":"boost_item","resourceId":1,"resourceQuantity":3}]}
            """));
        boxes.Add(JsonNode.Parse("""
            {"id":9904,"resourceBoxPurpose":"event_exchange",
             "details":[{"resourceType":"coin","resourceQuantity":1}]}
            """));
        JsonFiles.Write(path, boxes);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = original.DeepClone();
        state.Data.userEventItems = [new() { eventItemId = 1, quantity = 10 }];
        state.Data.userSkillPracticeTickets = [];
        state.Data.userEventExchanges = [new() { eventId = 1, eventExchangeId = 1, exchangeRemaining = 8, exchangeStatus = "exchangeable" }];
        state.Data.userEventMissions = [];
        store.Save(1, state);
        await ScenarioRunner.Run(client, new() { Steps = [Step(2), Step(1)] }, Path.Combine(directory, "event-exchange"));
        var saved = store.Read(1)!.Data;
        check(saved.userEventItems.Single().quantity == 4 && saved.userSkillPracticeTickets.Single().quantity == 3 &&
            saved.userEventExchanges.Single().exchangeRemaining == 5 && saved.userEventMissions.Single().progress == 6,
            "活动兑换按数量扣发、累计任务进度并减少限购次数");
        var response = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "event-exchange/002.json")))!["response"]!;
        check(response["obtainUserResources"]![0]!["quantity"]!.GetValue<int>() == 1 &&
            response["obtainUserResources"]![0]!["resourceLevel"] == null &&
            !response["updatedResources"]!["userEventMissions"]![0]!["isNewAchieved"]!.GetValue<bool>(),
            "兑换奖励省略零等级，未达成活动任务返回非新达成状态");
        var previous = store.Read(1)!;
        var boundary = previous.DeepClone();
        boundary.Data.userEventItems.Single().quantity = 10;
        boundary.Data.userGamedata.coin = 100;
        boundary.Data.userBoostItems = [];
        boundary.Data.userEventExchanges = [.. boundary.Data.userEventExchanges,
            new() { eventId = 1, eventExchangeId = 2, exchangeRemaining = 2, exchangeStatus = "exchangeable" },
            new() { eventId = 1, eventExchangeId = 3, exchangeStatus = "exchangeable" }];
        store.Save(1, boundary);
        await ScenarioRunner.Run(client, new() { Steps = [Step(2, 2), Step(2, 3), Step(6, 3)] }, Path.Combine(directory, "event-boundary"));
        var final = store.Read(1)!.Data;
        check(final.userBoostItems.Single().quantity == 6 && final.userGamedata.coin == 108 &&
            final.userEventItems.Single().quantity == 0 && final.userEventMissions.Single().progress == 16,
            "有限和无限兑换均按实际成本累计，余额归零保留库存项");
        var finalResponse = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "event-boundary/003.json")))!["response"]!;
        var finite = finalResponse["updatedResources"]!["userEventExchanges"]!.AsArray().Single(x => x!["eventExchangeId"]!.GetValue<int>() == 2)!;
        var unlimited = finalResponse["updatedResources"]!["userEventExchanges"]!.AsArray().Single(x => x!["eventExchangeId"]!.GetValue<int>() == 3)!;
        check(finite["exchangeRemaining"]!.GetValue<int>() == 0 && finite["exchangeStatus"]!.GetValue<string>() == "not_exchangeable" &&
            unlimited["exchangeRemaining"] == null && unlimited["exchangeStatus"]!.GetValue<string>() == "exchangeable",
            "售罄返回零余量，无限量商品省略余量且保持可兑换");
        check(finalResponse["obtainUserResources"]![0]!["resourceId"] == null,
            "金币兑换奖励省略资源编号");
        store.Save(1, previous);
        foreach (var (count, threshold, expected) in new[] { (3, false, 409), (6, false, 501), (1, true, 501) })
        {
            if (threshold)
            {
                var nearing = store.Read(1)!;
                nearing.Data.userEventMissions.Single().progress = 19;
                store.Save(1, nearing);
            }
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            using var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await rejected.Send(new() { Operation = "system" });
            var failed = false;
            try { await rejected.Send(Step(count)); }
            catch (ClientFailure) { failed = true; }
            check(failed && rejected.LastHttpStatus == expected && before.SequenceEqual(DumpSerializer.Serialize(store.Read(1)!.Data)),
                "活动余额不足及未核验边界不改变库存、次数或任务");
        }
        foreach (var id in new[] { 2, 3 })
        {
            var overflowing = boundary.DeepClone();
            if (id == 2) overflowing.Data.userBoostItems = [new() { userId = 1, boostItemId = 1, quantity = int.MaxValue }];
            else overflowing.Data.userGamedata.coin = int.MaxValue;
            store.Save(1, overflowing);
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            using var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await rejected.Send(new() { Operation = "system" });
            var failed = false;
            try { await rejected.Send(Step(1, id)); }
            catch (ClientFailure) { failed = true; }
            check(failed && before.SequenceEqual(DumpSerializer.Serialize(store.Read(1)!.Data)),
                "兑换奖励数量溢出时回滚已扣代币和任务进度");
        }
        store.Save(1, original);
        await client.Send(new() { Operation = "system" });
    }

    private static ScenarioStep Step(int count, int id = 1) => new()
    {
        Operation = "event-exchange", Args = new() { ["eventExchangeId"] = id.ToString() }, Query = new() { ["count"] = count.ToString() }
    };
}
