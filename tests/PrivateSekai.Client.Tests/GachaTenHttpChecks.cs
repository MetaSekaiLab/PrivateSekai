using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Storage;

internal static class GachaTenHttpChecks
{
    public static void WriteMaster(string directory)
    {
        var path = Path.Combine(directory, "gachas.json");
        var gachas = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        gachas[0]!["gachaBehaviors"]!.AsArray().Add(JsonNode.Parse("""
            {"id":5,"gachaBehaviorType":"normal","spinCount":10,"costResourceType":"jewel","costResourceQuantity":1000}
            """));
        JsonFiles.Write(path, gachas);
        foreach (var (table, row) in new[]
        {
            ("characterMissionV2s", """{"id":9702,"characterId":1,"characterMissionType":"waiting_room","parameterGroupId":9702}"""),
            ("characterMissionV2ParameterGroups", """{"id":9702,"seq":1,"requirement":2,"exp":1}"""),
            ("honorMissions", """{"id":9701,"honorMissionType":"duplicate_card_total","requirement":3}""")
        })
        {
            var file = Path.Combine(directory, table + ".json");
            var rows = JsonNode.Parse(File.ReadAllText(file))!.AsArray();
            rows.Add(JsonNode.Parse(row));
            JsonFiles.Write(file, rows);
        }
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userCards = [new() { cardId = 1, duplicateCount = 0 }];
        state.Data.userChargedCurrency = new() { free = 1000 };
        state.Data.userGachas = [];
        state.Data.userGachaCeilItems = [];
        state.Data.userHonorMissions = [];
        state.Data.userMissionStatuses = [];
        state.Data.userCharacterMissions = [];
        state.Data.userCharacterMissionStatuses = [];
        state.Data.userFriends = [new() { opponentUserId = 901, friendStatus = "friend" }];
        state.Data.userBeginnerMissionV2s = [new() { beginnerMissionV2Id = 96, progress = 1 }];
        state.Data.refreshableTypes = [];
        store.Save(1, state);
        var result = await client.Send(new() { Operation = "gacha-draw", Args = new() { ["gachaId"] = "1", ["gachaBehaviorId"] = "5" } });
        var refresh = result["updatedResources"]!;
        check(result["obtainPrizes"]!.AsArray().Count == 10 &&
            result["obtainPrizes"]!.AsArray().All(p => !p!["newFlg"]!.GetValue<bool>()) &&
            refresh["userCards"]![0]!["duplicateCount"]!.GetValue<int>() == 10 &&
            result["userGacha"]!["count"]!.GetValue<int>() == 1 && result["obtainGachaCeilItems"]![0]!["quantity"]!.GetValue<int>() == 10,
            "十张抽取HTTP返回全部重复卡、单次行为记录与十个天井");
        check(refresh["userCharacterMissionV2s"]!.AsArray().Single()!["achievedMissions"]!.AsArray().Single()!["missionId"]!.GetValue<int>() == 9702 &&
            refresh["userHonorMissions"]!.AsArray().Single()!["achievedMissionIds"]!.AsArray().Single()!.GetValue<int>() == 9701,
            "重复卡角色及称号任务的本次达成提示按实际类型返回");
        check(refresh["userBeginnerMissionV2s"] == null && store.Read(1)!.Data.userBeginnerMissionV2s.Single().progress == 1,
            "抽卡响应不额外执行好友新手任务刷新");
        using (var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await readback.Send(new() { Operation = "system" });
            var suite = await readback.Suite();
            check(suite["userCards"]![0]!["duplicateCount"]!.GetValue<int>() == 10 &&
                suite["userCharacterMissionV2s"]!.AsArray().Single()!["achievedMissions"]!.AsArray().Count == 0 &&
                suite["userHonorMissions"]!.AsArray().Single()!["achievedMissionIds"]!.AsArray().Count == 0 &&
                store.Read(1)!.Data.userBeginnerMissionV2s.Single().progress == 2,
                "独立Suite保留重复卡、清除临时达成提示，并在读取时正常累加好友进度");
        }
        store.Save(1, original);
    }
}
