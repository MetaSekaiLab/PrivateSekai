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

    public static void WriteCraftMaster(string directory)
    {
        foreach (var (table, row) in new[]
        {
            ("costume3ds", """{"id":1002,"characterId":1,"partType":"body","costume3dType":"normal"}"""),
            ("beginnerMissionV2s", """{"id":4,"beginnerMissionV2Type":"make_any_costume","requirement":1}"""),
            ("characterMissionV2s", """{"id":1003,"characterId":1,"characterMissionType":"collect_costume_3d","parameterGroupId":3}"""),
            ("characterMissionV2ParameterGroups", """{"id":3,"seq":1,"requirement":1}""")
        })
        {
            var path = Path.Combine(directory, table + ".json");
            var rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
            rows.Add(JsonNode.Parse(row));
            JsonFiles.Write(path, rows);
        }
        File.WriteAllText(Path.Combine(directory, "costume3dShopItems.json"), """
            [{"id":1001,"bodyCostume3dId":1002,"costs":[
              {"resourceType":"material","resourceId":11,"resourceQuantity":300},
              {"resourceType":"material","resourceId":12,"resourceQuantity":30}]}]
            """);
        File.WriteAllText(Path.Combine(directory, "honorMissions.json"),
            """[{"id":3101,"honorMissionType":"collect_costume_3d","requirement":50}]""");
    }

    public static async Task RunCraft(ProtocolClient client, MemoryUserStore store, IServiceProvider provider, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userCostume3dStatuses = [new() { costume3dId = 1002, status = "sale" }];
        state.Data.userCostume3dShopItems = [new() { costume3dShopItemId = 1001, status = "sale" }];
        state.Data.userMaterials = [new() { materialId = 11, quantity = 310 }, new() { materialId = 12, quantity = 29 }];
        state.Data.userCharacterMissions = [];
        state.Data.userCharacterMissionStatuses = [];
        state.Data.userHonorMissions = [];
        store.Save(1, state);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var service = scope.ServiceProvider.GetRequiredService<CostumeService>();
        var rejected = false;
        try { operation.Execute(1, () => service.Craft(1001)); }
        catch (ArgumentException) { rejected = true; }
        var unchanged = store.Read(1)!.Data;
        check(rejected && unchanged.userMaterials.Single(m => m.materialId == 11).quantity == 310 &&
            unchanged.userCostume3dStatuses.Single().status == "sale" && unchanged.userHonorMissions.Length == 0,
            "第二项制作材料不足时不扣除第一项、不改变服装和任务状态");
        state = store.Read(1)!;
        state.Data.userMaterials.Single(m => m.materialId == 12).quantity = 32;
        state.Data.userHonorMissions = [new() { honorMissionType = "collect_costume_3d", progress = 49, achievedMissionIds = [] }];
        store.Save(1, state);
        rejected = false;
        try { operation.Execute(1, () => service.Craft(1001)); }
        catch (NotSupportedException) { rejected = true; }
        unchanged = store.Read(1)!.Data;
        check(rejected && unchanged.userMaterials.Single(m => m.materialId == 11).quantity == 310 &&
            unchanged.userCostume3dStatuses.Single().status == "sale" && unchanged.userCostume3dShopItems.Single().status == "sale" &&
            unchanged.userHonorMissions.Single().progress == 49,
            "未核验的荣誉达成被拒绝时回滚已执行的扣材、发放及售罄状态");
        state = store.Read(1)!;
        state.Data.userHonorMissions = [];
        store.Save(1, state);
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = "costume-craft", Args = new() { ["shopItemId"] = "1001" }
        }] }, Path.Combine(directory, "costume-craft"));
        var saved = store.Read(1)!.Data;
        check(saved.userMaterials.Single(m => m.materialId == 11).quantity == 10 &&
            saved.userMaterials.Single(m => m.materialId == 12).quantity == 2 &&
            saved.userCostume3dStatuses.Single().status == "available" && saved.userCostume3dStatuses.Single().obtainedAt > 0 &&
            saved.userCostume3dShopItems.Single().status == "sold_out" && saved.userCharacterCostume3ds.Single().bodyCostume3dId == 2,
            "制作扣材并将已有待售服装转为持有，商品售罄且不自动穿戴");
        check(saved.userBeginnerMissionV2s.Single(m => m.beginnerMissionV2Id == 4).progress == 1 &&
            saved.userCharacterMissionStatuses.Single().missionStatus == "achieved" &&
            saved.userCharacterMissions.Single().achievedMissions.Length == 0 && saved.userHonorMissions.Single().progress == 1,
            "制作同步新手、角色收集和荣誉进度，角色临时达成列表不持久化");
        var response = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "costume-craft/001.json")))!["response"]!;
        check(response["consumedCosts"]![0]!["resourceLevel"] == null &&
            response["obtainedResources"]![0]!["resourceLevel"]!.GetValue<int>() == 0 &&
            response["updatedResources"]!["userCharacterMissionV2s"]![0]!["achievedMissions"]!.AsArray().Count == 1,
            "制作响应保留服装资源等级并携带角色任务临时达成列表");
        rejected = false;
        try { operation.Execute(1, () => service.Craft(1001)); }
        catch (ArgumentException) { rejected = true; }
        check(rejected && store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 11).quantity == 10,
            "已售罄服装不能重复制作扣材");
    }
}
