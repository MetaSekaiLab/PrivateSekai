extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class MaterialExchangeReplay
{
    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "materialExchanges", "materialExchangeSummaries", "resourceBoxes", "practiceTickets", "eventExchangeSummaries", "skillPracticeTickets", "eventMissions" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static TimeProvider Clock(string path) => new ReplayClock(JsonNode.Parse(File.ReadAllText(path))!
        ["response"]!["updatedResources"]!["now"]!.GetValue<long>());

    private sealed class ReplayClock(long now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(now);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string path, string output)
    {
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var operation = official["operation"]?.GetValue<string>();
        if (operation is not ("material-exchange" or "event-exchange") || official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的材料或活动兑换记录。");
        string[] fields = operation == "material-exchange"
            ? ["userMaterials", "userPracticeTickets", "userMaterialExchanges"]
            : ["userEventItems", "userSkillPracticeTickets", "userBoostItems", "userGamedata", "userEventExchanges", "userEventMissions"];
        var state = store.Read(1)!;
        var before = official["before"]!.DeepClone();
        foreach (var field in fields)
        {
            if (before[field] is JsonObject obj && obj["userId"] != null) obj["userId"] = 1;
            foreach (var item in before[field] as JsonArray ?? [])
                if (item?["userId"] != null) item["userId"] = 1;
        }
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => fields.Contains((string)m.Key)))
            if (before[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = operation,
            Args = JsonSerializer.Deserialize<Dictionary<string, string>>(official["args"]!.ToJsonString())!,
            Query = JsonSerializer.Deserialize<Dictionary<string, string>>(official["query"]!.ToJsonString())!
        }] }, output);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official, local));
        foreach (var record in new[] { official, local })
        {
            foreach (var side in new[] { "before", "after" }) record[side] = Select(record[side], fields);
            record["response"]!["updatedResources"] = Select(record["response"]!["updatedResources"], fields);
        }
        JsonFiles.Write(Path.Combine(output, operation + "-compare.json"), ScenarioRunner.Compare(official, local));
    }

    private static JsonObject Select(JsonNode? source, string[] fields)
    {
        var selected = new JsonObject(fields.Select(field => KeyValuePair.Create(field, source?[field]?.DeepClone())));
        if (selected["userGamedata"] is JsonObject data)
            selected["userGamedata"] = new JsonObject { ["coin"] = data["coin"]?.DeepClone() };
        return selected;
    }
}
