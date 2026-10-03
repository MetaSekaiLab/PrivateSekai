using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Client;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

internal static class CharacterMissionHttpChecks
{
    public static void WriteMaster(string directory)
    {
        foreach (var (table, json) in new[]
        {
            ("levels", """[{"levelType":"character","level":1,"totalExp":0},{"levelType":"character","level":2,"totalExp":1},{"levelType":"character","level":3,"totalExp":3},{"levelType":"character","level":4,"totalExp":100}]"""),
            ("resourceBoxes", """[{"id":1004,"resourceBoxPurpose":"character_rank_reward","details":[{"resourceType":"jewel","resourceQuantity":100}]},{"id":1005,"resourceBoxPurpose":"character_rank_reward","details":[{"resourceType":"honor","resourceId":1,"resourceLevel":1,"resourceQuantity":1},{"resourceType":"honor_background","resourceId":10101,"resourceQuantity":1},{"resourceType":"honor_word","resourceId":10101,"resourceQuantity":1}]}]""")
        })
        {
            var path = Path.Combine(directory, table + ".json");
            var rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
            foreach (var row in JsonNode.Parse(json)!.AsArray()) rows.Add(row!.DeepClone());
            JsonFiles.Write(path, rows);
        }
        File.WriteAllText(Path.Combine(directory, "characterRanks.json"),
            """[{"characterId":1,"characterRank":2,"rewardResourceBoxIds":[1004]},{"characterId":1,"characterRank":3,"rewardResourceBoxIds":[1005]}]""");
        var parameterPath = Path.Combine(directory, "characterMissionV2ParameterGroups.json");
        var parameters = JsonNode.Parse(File.ReadAllText(parameterPath))!.AsArray();
        parameters.Single(r => r!["id"]!.GetValue<int>() == 3)!["exp"] = 4;
        parameters.Add(JsonNode.Parse("""{"id":3,"seq":2,"requirement":2,"exp":4}"""));
        parameters.Add(JsonNode.Parse("""{"id":19,"seq":1,"requirement":1,"exp":1}"""));
        JsonFiles.Write(parameterPath, parameters);
        var missionPath = Path.Combine(directory, "characterMissionV2s.json");
        var missions = JsonNode.Parse(File.ReadAllText(missionPath))!.AsArray();
        missions.Add(JsonNode.Parse("""{"id":1019,"characterId":1,"characterMissionType":"collect_character_archive_voice","parameterGroupId":19}"""));
        JsonFiles.Write(missionPath, missions);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, IServiceProvider provider, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userCharacters = [new() { characterId = 1, characterRank = 1 }];
        state.Data.userCharacterMissionStatuses = [new() { userId = 1, characterId = 1, missionId = 1003, parameterGroupId = 3, seq = 1, missionStatus = "achieved" }];
        state.Data.userHonors = [];
        state.Data.userHonorBackgrounds = [new() { honorBackgroundId = 10101, obtainedAt = 1 }];
        state.Data.userHonorWords = [];
        state.Data.userChargedCurrency = new() { free = 10, paid = 0, paidUnitPrices = [] };
        state.Data.userProfile = new() { userId = 1, profileImageType = "leader" };
        state.Data.userProfileHonors = [];
        store.Save(1, state);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var missions = scope.ServiceProvider.GetRequiredService<MissionService>();
        var rejected = false;
        try { operation.Execute(1, () => missions.ReceiveCharacterMissions(1, "COLLECT_COSTUME_3D")); }
        catch (NotSupportedException) { rejected = true; }
        var saved = store.Read(1)!.Data;
        check(rejected && saved.userChargedCurrency.free == 10 && saved.userHonors.Length == 0 &&
            saved.userCharacters.Single().totalExp == 0 && saved.userCharacterMissionStatuses.Single().missionStatus == "achieved",
            "后续称号奖励拒绝时回滚先发宝石、称号及任务状态");
        state = store.Read(1)!;
        state.Data.userHonorBackgrounds = [];
        store.Save(1, state);
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = "character-mission-receive", Args = new() { ["characterId"] = "1", ["characterMissionType"] = "COLLECT_COSTUME_3D" }
        }] }, Path.Combine(directory, "character-mission"));
        saved = store.Read(1)!.Data;
        check(saved.userCharacters.Single().characterRank == 3 && saved.userCharacters.Single().totalExp == 4 &&
            saved.userCharacters.Single().exp == 1 && saved.userChargedCurrency.free == 110 &&
            saved.userCharacterMissionStatuses.Single().missionStatus == "received", "角色领奖跨级累加经验和奖励并保存已领取状态");
        check(saved.userHonors.Single().userId == 1 && saved.userHonors.Single().level == 1 &&
            saved.userHonors.Single().obtainedAt == saved.userHonorBackgrounds.Single().obtainedAt &&
            saved.userHonors.Single().obtainedAt == saved.userHonorWords.Single().obtainedAt,
            "称号、背景及文字保留同次发放时间与所属用户");
        var response = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "character-mission/001.json")))!["response"]!;
        check(response["reportedMissionStatuses"]![0]!["userId"] == null &&
            response["updatedResources"]!["userCharacterMissionV2Statuses"]![0]!["userId"] != null &&
            response["updatedResources"]!["userCharacters"]![0]!["userId"] == null,
            "领奖报告省略用户字段，持久任务状态保留，角色记录省略");
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = "character-mission-receive", Args = new() { ["characterId"] = "1", ["characterMissionType"] = "COLLECT_COSTUME_3D" }
        }] }, Path.Combine(directory, "character-mission-repeat"));
        var repeated = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "character-mission-repeat/001.json")))!["response"]!;
        check(repeated["reportedMissionStatuses"]!.AsArray().Count == 0 && repeated["updatedResources"]!["userCharacters"] != null &&
            repeated["updatedResources"]!["userCharacterMissionV2Statuses"] == null && repeated["updatedResources"]!["userHonors"] == null &&
            store.Read(1)!.Data.userChargedCurrency.free == 110 && store.Read(1)!.Data.userCharacters.Single().totalExp == 4,
            "指定任务重复领取返回空报告和角色刷新，不重复发奖或刷新任务状态");
        state = store.Read(1)!;
        state.Data.userCharacterMissionStatuses = [.. state.Data.userCharacterMissionStatuses,
            new() { userId = 1, characterId = 1, missionId = 1019, parameterGroupId = 19, seq = 1, missionStatus = "achieved" },
            new() { userId = 1, characterId = 1, missionId = 1003, parameterGroupId = 3, seq = 2, missionStatus = "achieved" },
            new() { userId = 1, characterId = 2, missionId = 2003, parameterGroupId = 3, seq = 1, missionStatus = "achieved" }];
        store.Save(1, state);
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = "character-mission-receive-all", Args = new() { ["characterId"] = "1" }
        }] }, Path.Combine(directory, "character-mission-all"));
        saved = store.Read(1)!.Data;
        response = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "character-mission-all/001.json")))!["response"]!;
        check(saved.userCharacters.Single().totalExp == 9 && saved.userCharacters.Single().exp == 6 && saved.userChargedCurrency.free == 110 &&
            saved.userCharacterMissionStatuses.Single(s => s.characterId == 2).missionStatus == "achieved",
            "全部领取合并两类新任务经验，跳过已领取项且不修改其他角色");
        check(response["reportedMissionStatuses"]!.AsArray().Select(s => s!["missionId"]!.GetValue<int>()).SequenceEqual(new[] { 1003, 1019 }) &&
            response["updatedResources"]!["userHonors"] == null,
            "全部领取只报告本次任务并按任务顺序返回，不重复发称号");
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = "character-mission-receive-all", Args = new() { ["characterId"] = "1" }
        }] }, Path.Combine(directory, "character-mission-all-repeat"));
        repeated = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "character-mission-all-repeat/001.json")))!["response"]!;
        check(repeated["reportedMissionStatuses"]!.AsArray().Count == 0 && repeated["updatedResources"]!["userCharacters"] != null &&
            repeated["updatedResources"]!["userCharacterMissionV2Statuses"] == null && store.Read(1)!.Data.userCharacters.Single().totalExp == 9,
            "全部任务重复领取成功返回空报告，经验保持不变");
        state = store.Read(1)!;
        state.Data.userCharacterMissionStatuses = [.. state.Data.userCharacterMissionStatuses,
            new() { userId = 1, characterId = 1, missionId = 999999, parameterGroupId = 1, seq = 1, missionStatus = "achieved" }];
        store.Save(1, state);
        rejected = false;
        try { operation.Execute(1, () => missions.ReceiveCharacterMissions(1, null)); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && store.Read(1)!.Data.userCharacters.Single().totalExp == 9,
            "全部领取遇到缺失定义的达成项时拒绝，不静默遗漏");
    }
}
