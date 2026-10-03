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
        foreach (var table in new[] { "cards", "challengeLiveCharacters", "challengeLiveDecks", "releaseConditions", "oneTimeBehaviors", "configs", "challengeLives", "musicDifficulties" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string path, string output)
    {
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var operation = official["operation"]?.GetValue<string>();
        if (operation is not ("challenge-deck-save" or "challenge-character-unlock" or "challenge-live-start") ||
            official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的官方挑战编队、首次解锁或开局记录。");
        var state = store.Read(1)!;
        var before = official["before"]!.DeepClone();
        if (before["userGamedata"] != null) before["userGamedata"]!["userId"] = 1;
        foreach (var behavior in before["userOneTimeBehaviors"]?.AsArray() ?? [])
            behavior!["userId"] = 1;
        foreach (var card in before["userCards"]?.AsArray() ?? [])
            if (card?["userId"] != null) card["userId"] = 1;
        var import = Fields.Concat(["userCards", "userCharacters", "userReleaseConditions", "userOneTimeBehaviors", "userGamedata"]).ToHashSet();
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => import.Contains((string)m.Key)))
            if (before[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = operation,
            Args = JsonSerializer.Deserialize<Dictionary<string, string>>(official["args"]!.ToJsonString())!,
            Body = official["request"]?.DeepClone().AsObject()
        }] }, output);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official, local));
        if (operation == "challenge-live-start")
        {
            foreach (var record in new[] { official, local })
            {
                var id = record["response"]!["userChallengeLiveId"]!.GetValue<string>();
                var statuses = record["after"]!["userChallengeLivePlayStatuses"]!.AsArray();
                if (statuses.Count(s => s!["userChallengeLiveId"]?.GetValue<string>() == id) != 1)
                    throw new InvalidOperationException("开局会话必须对应唯一参与状态。");
                record["response"]!["userChallengeLiveId"] = "<new-session>";
                foreach (var source in new[] { record["after"], record["response"]!["updatedResources"] })
                    foreach (var status in source!["userChallengeLivePlayStatuses"]!.AsArray())
                        if (status!["userChallengeLiveId"]?.GetValue<string>() == id)
                            status["userChallengeLiveId"] = "<new-session>";
            }
        }
        foreach (var record in new[] { official, local })
        {
            foreach (var side in new[] { "before", "after" }) record[side] = Select(record[side], operation);
            record["response"]!["updatedResources"] = Select(record["response"]!["updatedResources"], operation);
        }
        var report = operation switch
        {
            "challenge-deck-save" => "challenge-deck-compare.json",
            "challenge-live-start" => "challenge-start-compare.json",
            _ => "challenge-unlock-compare.json"
        };
        JsonFiles.Write(Path.Combine(output, report), ScenarioRunner.Compare(official, local));
    }

    public static TimeProvider Clock(string path) => new ReplayClock(JsonNode.Parse(File.ReadAllText(path))!
        ["response"]!["updatedResources"]!["now"]!.GetValue<long>());

    private sealed class ReplayClock(long now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(now);
    }

    private static JsonObject Select(JsonNode? source, string operation) => new(
        (operation != "challenge-character-unlock" ? Fields : Fields.Concat(["userReleaseConditions", "userOneTimeBehaviors"]))
        .Select(field => KeyValuePair.Create(field, source?[field]?.DeepClone())));
}
