using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class MissionHttpChecks
{
    public static void WriteMaster(string directory)
    {
        // 测试奖励数值，不用于官方场景。
        File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"),
            """[{"id":6,"requirement":1,"rewards":[{"resourceBoxId":41}]}]""");
        var path = Path.Combine(directory, "resourceBoxes.json");
        var boxes = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        boxes.Add(JsonNode.Parse("""{"id":41,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"coin","resourceQuantity":5}]}"""));
        JsonFiles.Write(path, boxes);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userGamedata.coin = 0;
        state.Data.userBeginnerMissionV2s = [new() { beginnerMissionV2Id = 6, progress = 1, isNewAchieved = true }];
        state.Data.userMissionStatuses = [new() { missionType = "beginner_mission_v2", missionId = 6, missionStatus = "achieved" }];
        store.Save(1, state);
        var scenario = new Scenario { Steps =
        [
            new() { Operation = "beginner-mission-receive", Body = JsonNode.Parse("""{"missionIds":[6]}""")!.AsObject(),
                Expect = new() { ["/obtainedRewards/0/quantity"] = JsonValue.Create(5) } }
        ] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "missions"));
        var saved = store.Read(1)!.Data;
        check(saved.userGamedata.coin == 5 && saved.userMissionStatuses.Single().missionStatus == "received",
            "新手任务客户端真实 HTTP 领取奖励并保存领取状态");
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "missions/001.json")))!;
        check(record["stateChanges"]!.AsArray().Count > 0 && record["response"]!["updatedResources"] != null,
            "任务领奖同时保存奖励响应与前后状态");
    }
}
