using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Client;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

internal static class CostumeHttpChecks
{
    public static void WriteMaster(string directory)
    {
        File.WriteAllText(Path.Combine(directory, "costume3ds.json"), """
            [{"id":1,"characterId":1,"partType":"head"},{"id":2,"characterId":1,"partType":"body"},
             {"id":3,"characterId":1,"partType":"hair"},{"id":4,"characterId":1,"partType":"body"}]
            """);
        var path = Path.Combine(directory, "beginnerMissionV2s.json");
        var rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        rows.Add(JsonNode.Parse("""{"id":5,"beginnerMissionV2Type":"change_any_character_costume","requirement":1}"""));
        JsonFiles.Write(path, rows);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, IServiceProvider provider, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userCharacterCostume3ds = [new() { characterId = 1, unit = "light_sound", headCostume3dId = 1, bodyCostume3dId = 4, hairCostume3dId = 3 }];
        state.Data.userCostume3dStatuses = [new() { costume3dId = 1, status = "available" },
            new() { costume3dId = 2, status = "available" }, new() { costume3dId = 3, status = "available" },
            new() { costume3dId = 4, status = "forbidden" }];
        state.Data.userBeginnerMissionV2s = [];
        state.Data.userMissionStatuses = [];
        store.Save(1, state);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var service = scope.ServiceProvider.GetRequiredService<CostumeService>();
        foreach (var body in new[] { 1, 4 })
        {
            var rejected = false;
            try
            {
                operation.Execute(1, () =>
                {
                    service.Save(1, "LIGHT_SOUND", new() { headCostume3dId = 1, bodyCostume3dId = body, hairCostume3dId = 3 });
                    return true;
                });
            }
            catch (ArgumentException) { rejected = true; }
            check(rejected && store.Read(1)!.Data.userCharacterCostume3ds.Single().bodyCostume3dId == 4 &&
                store.Read(1)!.Data.userMissionStatuses.Length == 0, "错误部位或未持有服装不能修改穿戴及任务");
        }
        var scenario = new Scenario { Steps = [new()
        {
            Operation = "character-costume-save", Args = new() { ["characterId"] = "1", ["unit"] = "LIGHT_SOUND" },
            Body = JsonNode.Parse("""{"headCostume3dId":1,"bodyCostume3dId":2,"hairCostume3dId":3}""")!.AsObject()
        }] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "costume"));
        var saved = store.Read(1)!.Data;
        check(saved.userCharacterCostume3ds.Single().bodyCostume3dId == 2 &&
            saved.userMissionStatuses.Single().missionStatus == "achieved" && !saved.userBeginnerMissionV2s.Single().isNewAchieved,
            "服装 HTTP 保存与任务达成持久化，临时提示不保存");
        var response = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "costume/001.json")))!["response"]!["updatedResources"]!;
        check(response["userBeginnerMissionV2s"]![0]!["isNewAchieved"]!.GetValue<bool>() && response["userCostume3dStatuses"] == null,
            "服装保存响应包含任务达成提示，不重复刷新持有列表");
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "costume-repeat"));
        var repeated = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "costume-repeat/001.json")))!["response"]!["updatedResources"]!;
        check(store.Read(1)!.Data.userBeginnerMissionV2s.Single().progress == 2 &&
            repeated["userBeginnerMissionV2s"]![0]!["progress"]!.GetValue<int>() == 2 &&
            !repeated["userBeginnerMissionV2s"]![0]!["isNewAchieved"]!.GetValue<bool>() &&
            repeated["userMissionStatuses"] == null,
            "同值服装保存仍累计进度，但不重复提示达成或刷新任务状态");
        var received = store.Read(1)!;
        received.Data.userMissionStatuses.Single().missionStatus = "received";
        store.Save(1, received);
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "costume-after-received"));
        var afterReceived = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "costume-after-received/001.json")))!["response"]!["updatedResources"]!;
        check(store.Read(1)!.Data.userBeginnerMissionV2s.Single().progress == 3 &&
            store.Read(1)!.Data.userMissionStatuses.Single().missionStatus == "received" &&
            afterReceived["userMissionStatuses"] == null && !afterReceived["userBeginnerMissionV2s"]![0]!["isNewAchieved"]!.GetValue<bool>(),
            "服装任务领奖后继续计数，保留已领取状态且不重复提示");
    }
}
