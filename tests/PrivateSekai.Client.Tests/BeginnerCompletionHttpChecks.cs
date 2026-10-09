using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class BeginnerCompletionHttpChecks
{
    public static void WriteMaster(string directory)
    {
        var path = Path.Combine(directory, "beginnerMissionV2s.json");
        var rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        rows.Add(JsonNode.Parse("""{"id":9871,"beginnerMissionV2Category":"normal","requirement":1}"""));
        rows.Add(JsonNode.Parse("""{"id":9872,"beginnerMissionV2Type":"achieve_all_missions","beginnerMissionV2Category":"complete","requirement":2,"rewards":[{"resourceBoxId":9872}]}"""));
        JsonFiles.Write(path, rows);
        path = Path.Combine(directory, "resourceBoxes.json");
        rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        rows.Add(JsonNode.Parse("""{"id":9872,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"coin","resourceQuantity":7}]}"""));
        JsonFiles.Write(path, rows);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userFriends = [];
        state.Data.userMissionStatuses = [new() { userId = 1, missionType = "beginner_mission_v2", missionId = 9871, missionStatus = "achieved" }];
        state.Data.userBeginnerMissionV2s = [new() { beginnerMissionV2Id = 9872, progress = 1 }];
        store.Save(1, state);
        var response = await client.Send(new() { Operation = "beginner-mission-receive",
            Body = JsonNode.Parse("""{"missionIds":[9871]}""")!.AsObject() });
        check(response["updatedResources"]!["userBeginnerMissionV2s"]!.AsArray()
                .Single(m => m!["beginnerMissionV2Id"]!.GetValue<int>() == 9872)!["isNewAchieved"]!.GetValue<bool>() &&
            store.Read(1)!.Data.userMissionStatuses.Single(m => m.missionId == 9872).missionStatus == "achieved",
            "正式领奖HTTP在总任务跨门槛时返回临时达成提示并保存状态");
        var after = await client.Suite();
        check(!after["userBeginnerMissionV2s"]!.AsArray()
                .Single(m => m!["beginnerMissionV2Id"]!.GetValue<int>() == 9872)!["isNewAchieved"]!.GetValue<bool>(),
            "独立Suite回读不保留总任务临时提示");
        await client.Send(new() { Operation = "beginner-mission-receive",
            Body = JsonNode.Parse("""{"missionIds":[9872]}""")!.AsObject() });
        check(store.Read(1)!.Data.userGamedata.coin == original.Data.userGamedata.coin + 7 &&
            store.Read(1)!.Data.userMissionStatuses.Single(m => m.missionId == 9872).missionStatus == "received" &&
            store.Read(1)!.Data.userBeginnerMissionV2s.Single(m => m.beginnerMissionV2Id == 9872).progress == 2,
            "总任务可通过独立HTTP请求领奖，不额外增加总进度");
        store.Save(1, original);
    }
}
