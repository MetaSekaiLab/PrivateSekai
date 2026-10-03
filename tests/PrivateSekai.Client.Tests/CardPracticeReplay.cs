extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class CardPracticeReplay
{
    private static readonly string[] Fields = ["userCards", "userPracticeTickets", "userBeginnerMissionV2s", "userMissionStatuses",
        "userMaterials", "userCharacterMissionV2s", "userCharacterMissionV2Statuses"];

    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "cards", "cardRarities", "levels", "practiceTickets", "beginnerMissionV2s", "cardEpisodes", "releaseConditions",
                     "characterMissionV2s", "characterMissionV2ParameterGroups", "resourceBoxes" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string path, string output)
    {
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var operation = official["operation"]?.GetValue<string>();
        if (operation is not ("card-practice" or "special-training") || official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的卡牌练习或特训记录。");
        var state = store.Read(1)!;
        var before = official["before"]!.DeepClone();
        foreach (var field in Fields)
            foreach (var item in before[field]?.AsArray() ?? [])
                if (item?["userId"] != null) item["userId"] = 1;
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => Fields.Contains((string)m.Key)))
            if (before[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = operation,
            Args = JsonSerializer.Deserialize<Dictionary<string, string>>(official["args"]!.ToJsonString())!,
            Body = official["request"]!.DeepClone().AsObject()
        }] }, output);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official, local));
        foreach (var record in new[] { official, local })
        {
            foreach (var side in new[] { "before", "after" }) record[side] = Select(record[side]);
            record["response"]!["updatedResources"] = Select(record["response"]!["updatedResources"]);
        }
        JsonFiles.Write(Path.Combine(output, operation + "-compare.json"), ScenarioRunner.Compare(official, local));
    }

    private static JsonObject Select(JsonNode? source) => new(Fields.Select(field => KeyValuePair.Create(field, source?[field]?.DeepClone())));
}
