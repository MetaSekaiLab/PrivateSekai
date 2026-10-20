extern alias game;

using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class EventMissionHttpChecks
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
        Add("events", """{"id":926,"startAt":0,"closedAt":4102444800000}""");
        Add("events", """{"id":927,"startAt":0,"closedAt":1}""");
        Add("liveMissions", """{"id":926001,"liveMissionPeriodId":1,"liveMissionType":"free","requirement":2000000000}""");
        Add("resourceBoxes", """{"id":9261,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"live_point","resourceQuantity":50}]}""");
        Add("resourceBoxes", """
            {"id":9262,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"jewel","resourceQuantity":10},
             {"resourceType":"gacha_ticket","resourceId":17,"resourceQuantity":2},{"resourceType":"material","resourceId":281,"resourceQuantity":1},
             {"resourceType":"live_point","resourceQuantity":50}]}
            """);
        Add("resourceBoxes", """{"id":9263,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"future","resourceId":1,"resourceQuantity":1}]}""");
        for (var id = 1; id <= 7; id++)
            Add("eventMissions", new JsonObject { ["id"] = 92600 + id, ["eventId"] = id == 5 ? 927 : 926,
                ["eventMissionCategory"] = id == 3 ? "complete_selectable_reward" : "normal",
                ["rewards"] = new JsonArray(new JsonObject { ["missionType"] = "event_mission", ["missionId"] = 92600 + id,
                    ["resourceBoxId"] = id == 2 ? 9262 : id == 4 ? 9263 : 9261 }) }.ToJsonString());
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        void Reset(int progress = 1999999975)
        {
            var state = original.DeepClone();
            state.Data.userChargedCurrency = new() { free = 100, paid = 7 };
            state.Data.userGachaTickets = [];
            state.Data.userMaterials = [];
            state.Data.userLiveMissions = [new() { liveMissionPeriodId = 1, liveMissionStatus = "free", progress = progress, achievedMissionIds = [] }];
            state.Data.userMissionStatuses = [.. Enumerable.Range(1, 6).Select(id => new game::Sekai.UserMissionStatus {
                missionType = "event_mission", missionId = 92600 + id, missionStatus = "achieved" }),
                new() { missionType = "live_mission", missionId = 1, missionStatus = "received" }];
            state.Data.userEventMissions = [new() { eventId = 926, eventMissionId = 92601, progress = 10, isNewAchieved = false }];
            state.Data.refreshableTypes = [];
            store.Save(1, state);
        }
        ScenarioStep Receive(params int[] ids) => new()
        {
            Operation = "event-mission-receive", Body = new JsonObject { ["missionIds"] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) }
        };
        Reset();
        check(Operations.Path(Operations.All["event-mission-receive"], Receive(92601), 1) == "/api/user/1/mission/event_mission"
            && Operations.All["event-mission-receive"].Method == "PUT", "活动任务领奖使用通用任务原路由和PUT方法");
        var output = Path.Combine(directory, "event-mission");
        await ScenarioRunner.Run(client, new() { Steps = [Receive(92601), Receive(92602, 92606)] }, output);
        var first = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!["response"]!;
        check(first["obtainedRewards"]!.AsArray().Single()!["resourceType"]!.GetValue<string>() == "live_point"
            && first["obtainedRewards"]![0]!["quantity"]!.GetValue<int>() == 50, "HTTP活动领奖返回完整Live P奖励");
        check(first["updatedResources"]!["userLiveMissions"]!.AsArray().Single()!["achievedMissionIds"]!.AsArray()
            .Single()!.GetValue<int>() == 926001, "活动Live P跨门槛在本次响应给出Live任务提示");
        var second = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "002.json")))!["response"]!;
        check(second["obtainedRewards"]!.AsArray().Select(r => r!["resourceType"]!.GetValue<string>()).SequenceEqual(
            new[] { "jewel", "gacha_ticket", "material", "live_point", "live_point" })
            && second["updatedResources"]!["userLiveMissions"]!.AsArray().Single()!["achievedMissionIds"]!.AsArray().Count == 0,
            "批量任务保留箱顺序和全部奖品，已达成Live任务不重复提示");
        var data = store.Read(1)!.Data;
        check(data.userLiveMissions.Single().progress == 2000000125 && data.userChargedCurrency.free == 110 && data.userChargedCurrency.paid == 7
            && data.userGachaTickets.Single().quantity == 2 && data.userMaterials.Single().quantity == 1,
            "HTTP批量活动领奖正确保存点数、免费宝石、抽卡券和材料");
        check(data.userLiveMissions.Single().achievedMissionIds.Length == 0 && data.userEventMissions.Single().progress == 10
            && data.userMissionStatuses.Count(s => s.missionType == "event_mission" && s.missionStatus == "received") == 3,
            "任务领奖只改变领取状态，保留进度且临时提示不入库");
        var pending = store.Read(1)!;
        pending.Data.refreshableTypes = ["userMaterials"];
        store.Save(1, pending);
        foreach (var (ids, expected) in new[] { (new[] { 92601 }, 409), (new[] { 92607 }, 409), (new[] { 92603 }, 501),
            (new[] { 92604 }, 501), (new[] { 92605 }, 409), (new[] { 92602, 92602 }, 409), (Array.Empty<int>(), 400), (new[] { 999999 }, 400) })
        {
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            using var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await rejected.Send(new() { Operation = "system" });
            try { await rejected.Send(Receive(ids)); } catch (ClientFailure) { }
            check(rejected.LastHttpStatus == expected && rejected.LastResponse?["httpStatus"]?.GetValue<int>() == expected
                && rejected.LastResponse?["errorCode"]?.GetValue<string>() == "" && rejected.LastResponse?["errorMessage"]?.GetValue<string>() == ""
                && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "活动任务重复、未达成、过期、选择分支和非法请求经HTTP拒绝且整单不变");
        }
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            var suite = await fresh.Suite();
            check(suite["userLiveMissions"]!.AsArray().Single()!["progress"]!.GetValue<int>() == 2000000125
                && suite["userLiveMissions"]![0]!["achievedMissionIds"]!.AsArray().Count == 0
                && suite["userMissionStatuses"]!.AsArray().Any(s => s!["missionType"]!.GetValue<string>() == "event_mission"
                    && s["missionId"]!.GetValue<int>() == 92602 && s["missionStatus"]!.GetValue<string>() == "received"),
                "独立认证Suite回读活动已领奖和Live点数，提示恢复为空");
        }
        Reset();
        var paid = store.Read(1)!;
        paid.Data.userLiveMissions.Single().liveMissionStatus = "premium";
        store.Save(1, paid);
        using (var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await rejected.Send(new() { Operation = "system" });
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await rejected.Send(Receive(92602)); } catch (ClientFailure) { }
            check(rejected.LastHttpStatus == 501 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "付费轨道的未核验点数奖励经HTTP拒绝，宝石、券和材料也不部分发放");
        }
        Reset(int.MaxValue - 25);
        using (var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await rejected.Send(new() { Operation = "system" });
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await rejected.Send(Receive(92602)); } catch (ClientFailure) { }
            check(rejected.LastHttpStatus == 500 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "HTTP最后Live P溢出回滚前面宝石、券、材料、点数和任务状态");
        }
        store.Save(1, original);
        await client.Send(new() { Operation = "system" });
    }
}
