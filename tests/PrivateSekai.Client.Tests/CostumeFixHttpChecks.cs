extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class CostumeFixHttpChecks
{
    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userFixCostumes = [];
        store.Save(1, state);
        ScenarioStep Fix(int id, string? unit) => new() { Operation = "costume-fix", Args = new() { ["characterId"] = id.ToString() }, Body = new() { ["unit"] = unit } };
        ScenarioStep Unfix(int id) => new() { Operation = "costume-unfix", Args = new() { ["characterId"] = id.ToString() } };
        var scenario = new Scenario { Steps = [Fix(21, "piapro"), Fix(21, "piapro"), Fix(21, "light_sound"), Fix(22, "idol"),
            Fix(0, "none"), Fix(int.MaxValue, "piapro"), Unfix(21), Unfix(22), Unfix(0), Unfix(int.MaxValue)] };
        var output = Path.Combine(directory, "costume-fix");
        await ScenarioRunner.Run(client, scenario, output);
        JsonNode Record(int index) => JsonNode.Parse(File.ReadAllText(Path.Combine(output, $"{index:D3}.json")))!;
        check(Record(1)["response"]!["updatedResources"]!["userFixCostumes"]![0]!["userId"]!.GetValue<string>() == "<redacted>" &&
            Record(2)["response"]!["updatedResources"]!["userFixCostumes"]!.AsArray().Count == 1,
            "固定PUT与重复PUT通过加密HTTP返回完整列表并保留所属用户字段脱敏");
        check(Record(3)["after"]!["userFixCostumes"]![0]!["unit"]!.GetValue<string>() == "light_sound" &&
            Record(4)["after"]!["userFixCostumes"]!.AsArray().Count == 2, "独立Suite回读保持组合替换和不同角色设置");
        check(Record(6)["after"]!["userFixCostumes"]![0]!["characterId"]!.GetValue<int>() == 0 &&
            Record(6)["after"]!["userFixCostumes"]!.AsArray().Last()!["characterId"]!.GetValue<int>() == int.MaxValue,
            "固定接口接受零及未知角色，按编号排序，none不转换成空值");
        check(Record(7)["response"]!["updatedResources"]!["userFixCostumes"]!.AsArray().Count == 3 &&
            Record(10)["response"]!["updatedResources"]!["userFixCostumes"]!.AsArray().Count == 0 &&
            store.Read(1)!.Data.userFixCostumes.Length == 0, "DELETE返回剩余完整列表，取消最后一项返回空数组并持久化");
        check(Record(7)["request"] == null && Operations.All["costume-unfix"].Method == "DELETE" &&
            Operations.Path(Operations.All["costume-fix"], scenario.Steps[0], 1) == "/api/user/1/fix-costume/21",
            "固定使用PUT和dump组合字段，取消使用DELETE且无请求体");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        foreach (var entry in new[] { (Step: Fix(21, "unknown"), Empty: true), (Step: Fix(21, "PIAPRO"), Empty: true),
            (Step: Fix(21, "any"), Empty: true), (Step: Fix(21, ""), Empty: true), (Step: Fix(21, null), Empty: false),
            (Step: Unfix(21), Empty: false) })
        {
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var failed = false;
            try { await invalid.Send(entry.Step); } catch (ClientFailure) { failed = true; }
            var status = entry.Step.Operation == "costume-unfix" ? 404 : 400;
            check(failed && invalid.LastHttpStatus == status && (entry.Empty ? invalid.LastResponse?["byteCount"]?.GetValue<int>() == 0 :
                invalid.LastResponse?["errorCode"]?.GetValue<string>() == "" && invalid.LastResponse?["errorMessage"]?.GetValue<string>() == ""),
                "固定非法组合400空正文，缺失组合400错误对象，取消未固定角色404错误对象");
        }
        check(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "固定和取消的HTTP拒绝不改变状态");
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }
}
