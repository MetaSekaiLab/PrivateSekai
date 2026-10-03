using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Storage;

internal static class MissionHttpChecks
{
    public static void WriteMaster(string directory)
    {
        // 测试奖励数值，不用于官方场景。
        File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"),
            """[{"id":1,"beginnerMissionV2Type":"any_live_clear","requirement":1},{"id":6,"beginnerMissionV2Type":"any_card_level_up","requirement":1,"rewards":[{"resourceBoxId":41}]},{"id":7,"beginnerMissionV2Type":"read_both_of_card_story","requirement":1}]""");
        var path = Path.Combine(directory, "resourceBoxes.json");
        var boxes = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        boxes.Add(JsonNode.Parse("""{"id":41,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"coin","resourceQuantity":5}]}"""));
        JsonFiles.Write(path, boxes);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store, string directory, Action<bool, string> check)
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
        var pending = store.Read(1)!;
        pending.Data.refreshableTypes = ["userMaterials"];
        store.Save(1, pending);
        using var duplicateClient = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await duplicateClient.Send(new() { Operation = "system" });
        var rejected = false;
        try
        {
            await duplicateClient.Send(new() { Operation = "beginner-mission-receive", Body = JsonNode.Parse("""{"missionIds":[6]}""")!.AsObject() });
        }
        catch (ClientFailure) { rejected = true; }
        check(rejected && duplicateClient.LastHttpStatus == 409 && duplicateClient.LastResponse?["httpStatus"]?.GetValue<int>() == 409 &&
            duplicateClient.LastResponse?["errorCode"]?.GetValue<string>() == "" && duplicateClient.LastResponse?["errorMessage"]?.GetValue<string>() == "",
            "新手任务重复领取返回官方已核验的 409 空错误码与文案");
        var repeated = store.Read(1)!;
        check(repeated.Data.userGamedata.coin == 5 && repeated.Data.userMissionStatuses.Single().missionStatus == "received" &&
            repeated.Data.refreshableTypes.SequenceEqual(new[] { "userMaterials" }),
            "重复领奖不发奖、不修改任务或消费待刷新字段");
        await client.Send(new() { Operation = "system" });
    }
}
