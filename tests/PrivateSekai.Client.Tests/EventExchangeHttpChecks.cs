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
              "resourceBoxId":9902,"eventExchangeCost":{"resourceType":"event_item","resourceId":1,"resourceQuantity":2}}]}]
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
        foreach (var (count, threshold, expected) in new[] { (3, false, 409), (5, false, 501), (1, true, 501) })
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
        store.Save(1, original);
        await client.Send(new() { Operation = "system" });
    }

    private static ScenarioStep Step(int count) => new()
    {
        Operation = "event-exchange", Args = new() { ["eventExchangeId"] = "1" }, Query = new() { ["count"] = count.ToString() }
    };
}
