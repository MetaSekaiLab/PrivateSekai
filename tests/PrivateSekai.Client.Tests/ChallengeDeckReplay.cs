extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class ChallengeDeckReplay
{
    private static readonly string[] Fields = ["userChallengeLivePlayDay", "userChallengeLivePlayStatuses", "userChallengeLiveSoloDecks",
        "userChallengeLiveSoloResults", "userChallengeLiveSoloStages", "userChallengeLiveSoloHighScoreRewards"];

    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "cards", "challengeLiveCharacters", "challengeLiveDecks", "releaseConditions" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string path, string output)
    {
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (official["operation"]?.GetValue<string>() != "challenge-deck-save" ||
            official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的官方挑战编队保存记录。");
        var state = store.Read(1)!;
        var before = official["before"]!.DeepClone();
        foreach (var card in before["userCards"]?.AsArray() ?? [])
            if (card?["userId"] != null) card["userId"] = 1;
        var import = Fields.Concat(["userCards", "userCharacters", "userReleaseConditions"]).ToHashSet();
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => import.Contains((string)m.Key)))
            if (before[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = "challenge-deck-save",
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
        JsonFiles.Write(Path.Combine(output, "challenge-deck-compare.json"), ScenarioRunner.Compare(official, local));
    }

    private static JsonObject Select(JsonNode? source) => new(Fields.Select(field => KeyValuePair.Create(field, source?[field]?.DeepClone())));
}
