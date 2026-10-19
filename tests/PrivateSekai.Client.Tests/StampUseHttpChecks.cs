using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class StampUseHttpChecks
{
    public static void WriteMaster(string directory)
    {
        foreach (var entry in new[]
        {
            (File: "stamps.json", Rows: """[{"id":80001,"stampType":"illustration"},{"id":80002,"stampType":"illustration"},{"id":80003,"stampType":"cheerful_carnival_message"},{"id":80004,"stampType":"illustration"}]"""),
            (File: "honorMissions.json", Rows: """[{"id":9501,"honorMissionType":"multi_live_stamp","requirement":3,"rewards":[{"resourceBoxId":9501}]}]"""),
            (File: "resourceBoxes.json", Rows: """[{"id":9501,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"honor","resourceId":134,"resourceLevel":1,"resourceQuantity":1}]}]""")
        })
        {
            var path = Path.Combine(directory, entry.File);
            var rows = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsArray() : new JsonArray();
            foreach (var row in JsonNode.Parse(entry.Rows)!.AsArray()) rows.Add(row!.DeepClone());
            JsonFiles.Write(path, rows);
        }
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userStamps = [new() { stampId = 80001 }, new() { stampId = 80004 }];
        state.Data.userHonorMissions = [];
        state.Data.userMissionStatuses = [];
        state.Data.userHonors = [];
        state.Data.userProfile = new() { userId = 1, profileImageType = "leader" };
        state.Data.userProfileHonors = [];
        state.Data.userFriends = [];
        state.Data.refreshableTypes = [];
        store.Save(1, state);
        ScenarioStep Report(string rows) => new() { Operation = "stamp-use-history",
            Body = JsonNode.Parse("{\"userStampUseHistories\":" + rows + "}")!.AsObject() };
        var scenario = new Scenario { Steps =
        [
            Report("""[{"stampId":80001,"useCount":1}]"""),
            Report("""[{"stampId":80001,"useCount":1}]"""),
            Report("""[{"stampId":80003,"useCount":1}]"""),
            new() { Operation = "honor-mission-receive", Body = JsonNode.Parse("""{"missionIds":[9501]}""")!.AsObject() },
            Report("""[{"stampId":80001,"useCount":2},{"stampId":80004,"useCount":-1}]"""),
            Report("""[{"stampId":80001,"useCount":-1}]"""), Report("[]")
        ] };
        var output = Path.Combine(directory, "stamp-use");
        await ScenarioRunner.Run(client, scenario, output);
        JsonNode Record(int index) => JsonNode.Parse(File.ReadAllText(Path.Combine(output, $"{index:D3}.json")))!;
        check(Operations.All["stamp-use-history"].Method == "POST" &&
            Operations.Path(Operations.All["stamp-use-history"], scenario.Steps[0], 1) == "/api/user/1/stamp-use-history" &&
            Record(1)["request"]!["userStampUseHistories"]![0]!.AsObject().Count == 2,
            "表情上报使用POST和dump列表元素，请求不添加所属用户字段");
        check(Record(1)["response"]!["updatedResources"]!["userHonorMissions"]![0]!["progress"]!.GetValue<int>() == 1 &&
            Record(2)["after"]!["userHonorMissions"]![0]!["progress"]!.GetValue<int>() == 2,
            "加密HTTP上报创建进度，重复批次继续累计并由Suite独立回读");
        check(Record(3)["response"]!["updatedResources"]!["userHonorMissions"]![0]!["achievedMissionIds"]![0]!.GetValue<int>() == 9501 &&
            Record(3)["after"]!["userHonorMissions"]![0]!["achievedMissionIds"]!.AsArray().Count == 0 &&
            Record(3)["after"]!["userMissionStatuses"]!.AsArray().Any(s => s!["missionId"]!.GetValue<int>() == 9501 && s["missionStatus"]!.GetValue<string>() == "achieved"),
            "未持有Cheerful临时表情推进门槛，本次提示与持久任务状态分别正确");
        check(Record(4)["response"]!["obtainedRewards"]![0]!["resourceId"]!.GetValue<int>() == 134 &&
            Record(4)["after"]!["userHonors"]!.AsArray().Any(h => h!["honorId"]!.GetValue<int>() == 134),
            "表情任务达成后通过同一客户端领奖并回读称号");
        check(Record(5)["after"]!["userHonorMissions"]![0]!["progress"]!.GetValue<int>() == 4 &&
            Record(5)["response"]!["updatedResources"]!["userHonorMissions"]![0]!["achievedMissionIds"]!.AsArray().Count == 0 &&
            Record(5)["after"]!["userMissionStatuses"]!.AsArray().Any(s => s!["missionId"]!.GetValue<int>() == 9501 && s["missionStatus"]!.GetValue<string>() == "received"),
            "领奖后按批次合计继续累计，保留已领取状态且不重复提示");
        check(Record(6)["response"]!["updatedResources"]!["userHonorMissions"] == null &&
            Record(7)["response"]!["updatedResources"]!["userHonorMissions"] == null &&
            store.Read(1)!.Data.userHonorMissions.Single().progress == 4,
            "负数与空批次不倒扣进度，也不返回表情称号刷新字段");
        state = store.Read(1)!;
        state.Data.refreshableTypes = ["userMaterials"];
        store.Save(1, state);
        var before = DumpSerializer.Serialize(state.Data);
        foreach (var entry in new[]
        {
            (Step: new ScenarioStep { Operation = "stamp-use-history", Body = new() }, Status: 500),
            (Step: Report("[null]"), Status: 500),
            (Step: Report("""[{"stampId":80002,"useCount":1},{"stampId":80002,"useCount":1}]"""), Status: 500),
            (Step: Report("""[{"stampId":80002,"useCount":0}]"""), Status: 409),
            (Step: Report("""[{"stampId":80001,"useCount":2},{"stampId":2147483647,"useCount":1}]"""), Status: 409),
            (Step: new ScenarioStep { Operation = "honor-mission-receive", Body = JsonNode.Parse("""{"missionIds":[9501]}""")!.AsObject() }, Status: 409)
        })
        {
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var failed = false;
            try { await invalid.Send(entry.Step); } catch (ClientFailure) { failed = true; }
            check(failed && invalid.LastHttpStatus == entry.Status && (entry.Status == 500 ?
                invalid.LastResponse?["byteCount"]?.GetValue<int>() == 0 :
                invalid.LastResponse?["errorCode"]?.GetValue<string>() == "" && invalid.LastResponse?["errorMessage"]?.GetValue<string>() == ""),
                "表情上报非法列表500空正文，持有条件不满足及重复领奖409空错误码和文案");
            check(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "HTTP整单拒绝不修改表情进度、奖励、任务或消费待刷新字段");
        }
        await client.Send(new() { Operation = "system" });
        var readback = await client.Suite();
        check(readback["userHonorMissions"]![0]!["progress"]!.GetValue<int>() == 4 &&
            store.Read(1)!.Data.userStamps.Length == 2 && store.Read(1)!.Data.userHonors.Length == 1,
            "拒绝后重新建立会话回读保持进度与奖励，不授予临时表情或重复称号");
        store.Save(1, original);
    }
}
