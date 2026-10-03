extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class BoostReplay
{
    private static int naturalMaximum;
    private static readonly string[] Fields = ["userBoost", "userBoostItems", "userColorfulPassV2"];

    public static TimeProvider NaturalClock(string path) => new ReplayClock(
        JsonNode.Parse(File.ReadAllText(path))!["response"]!["now"]!.GetValue<long>());

    private sealed class ReplayClock(long now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(now);
    }

    public static async Task RunNatural(ProtocolClient client, MemoryUserStore store, string beforePath, string afterPath, string output)
    {
        var before = JsonNode.Parse(File.ReadAllText(beforePath))!.AsObject();
        var after = JsonNode.Parse(File.ReadAllText(afterPath))!.AsObject();
        if (new[] { before, after }.Any(r => r["operation"]?.GetValue<string>() != "suite" || r["status"]?.GetValue<string>() != "completed"))
            throw new InvalidOperationException("自然恢复重放需要同一账号两次成功的状态读取，中间不能有体力写操作。");
        var state = store.Read(1)!;
        state.Data.userBoost = JsonSerializer.Deserialize<Boost>(before["response"]!["userBoost"]!.ToJsonString(), DumpJson.Options)!;
        if (before["response"]?["userColorfulPassV2"] is JsonObject pass)
            state.Data.userColorfulPassV2 = JsonSerializer.Deserialize<UserColorfulPassV2>(pass.ToJsonString(), DumpJson.Options);
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new() { Operation = "suite" }, new() { Operation = "suite" }] }, output);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        var repeated = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "002.json")))!.AsObject();
        JsonFiles.Write(Path.Combine(output, "natural-boost-compare.json"), new JsonObject
        {
            ["httpStatusDifferences"] = JsonSerializer.SerializeToNode(Comparison.Diff(after["httpStatus"], local["httpStatus"]), JsonFiles.Options),
            ["responseDifferences"] = JsonSerializer.SerializeToNode(Comparison.Diff(after["response"]!["userBoost"], local["response"]!["userBoost"]), JsonFiles.Options),
            ["repeatDifferences"] = JsonSerializer.SerializeToNode(Comparison.Diff(local["response"]!["userBoost"], repeated["response"]!["userBoost"]), JsonFiles.Options)
        });
    }

    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "boostItems", "configs" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
        naturalMaximum = int.Parse(JsonNode.Parse(File.ReadAllText(Path.Combine(source, "configs.json")))!.AsArray()
            .Single(c => c!["configKey"]!.GetValue<string>() == "boost_recovery_max_count")!["value"]!.GetValue<string>());
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string path, string output)
    {
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (official["operation"]?.GetValue<string>() != "boost-item" || official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的体力道具恢复记录。");
        var before = official["before"]!.DeepClone();
        foreach (var item in before["userBoostItems"]!.AsArray()) item!["userId"] = 1;
        if (before["userColorfulPassV2"] is JsonObject pass && pass.ContainsKey("userId")) pass["userId"] = 1;
        var state = store.Read(1)!;
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => Fields.Contains((string)m.Key)))
            if (before[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = "boost-item", Body = official["request"]!.DeepClone().AsObject()
        }] }, output);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official, local));
        foreach (var record in new[] { official, local })
        {
            foreach (var side in new[] { "before", "after" })
            {
                record[side] = Select(record[side]);
                // 满体力后的查询会更新时间，结算响应仍按原值精确比较。
                if (side == "after" && record[side]?["userBoost"] is JsonObject boost &&
                    boost["current"]!.GetValue<int>() >= naturalMaximum) boost.Remove("recoveryAt");
            }
            record["response"]!["updatedResources"] = Select(record["response"]!["updatedResources"]);
        }
        JsonFiles.Write(Path.Combine(output, "boost-compare.json"), ScenarioRunner.Compare(official, local));
    }

    private static JsonObject? Select(JsonNode? source) => source == null ? null : new(
        Fields.Where(field => source.AsObject().ContainsKey(field))
            .Select(field => KeyValuePair.Create(field, source[field]?.DeepClone())));
}
