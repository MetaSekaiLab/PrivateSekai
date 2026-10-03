extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class LoginBonusReplay
{
    private static readonly string[] Fields = ["userLoginBonuses", "userPresents", "userHonorMissions", "userGamedata"];

    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "configs", "loginBonuses", "beginnerLoginBonusSummaries", "beginnerLoginBonuses", "limitedLoginBonuses", "resourceBoxes" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static TimeProvider Clock(string path) => new FixedClock(Read(path)["response"]!["updatedResources"]!["now"]!.GetValue<long>());
    private sealed class FixedClock(long now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(now);
    }

    private static JsonObject Read(string path)
    {
        var result = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (result["operation"]?.GetValue<string>() != "home-refresh" || result["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的官方首页刷新记录。");
        return result;
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string path, string output)
    {
        var official = Read(path);
        var before = official["before"]!.DeepClone();
        BindUser(before);
        var state = store.Read(1)!;
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => Fields.Contains((string)m.Key)))
            if (before[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = "home-refresh", Body = official["request"]!.DeepClone().AsObject()
        }] }, output);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official, local));
        foreach (var record in new[] { official, local })
        {
            foreach (var side in new[] { "before", "after" }) record[side] = Select(record[side]);
            record["response"]!["updatedResources"] = Select(record["response"]!["updatedResources"]);
        }
        JsonFiles.Write(Path.Combine(output, "login-bonus-compare.json"), ScenarioRunner.Compare(official, local));
    }

    private static JsonObject Select(JsonNode? value)
    {
        var result = new JsonObject(Fields.Select(f => KeyValuePair.Create(f, value?[f]?.DeepClone())));
        if (result["userPresents"] is JsonArray presents)
        {
            // UUID 各端独立生成；按完整礼物内容对齐，保留重复礼物数量及所有业务字段差异。
            foreach (var present in presents) present!.AsObject().Remove("presentId");
            var ordered = presents.OrderBy(p => Comparison.Normalize(p)!.ToJsonString(), StringComparer.Ordinal).ToArray();
            for (var i = 0; i < ordered.Length; i++) ordered[i]!["presentId"] = $"present-{i}";
            result["userPresents"] = new JsonArray(ordered.Select(p => p!.DeepClone()).ToArray());
        }
        return result;
    }

    private static void BindUser(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToArray())
                if (pair.Key == "userId") obj[pair.Key] = 1;
                else BindUser(pair.Value);
        else if (node is JsonArray array)
            foreach (var item in array) BindUser(item);
    }
}
