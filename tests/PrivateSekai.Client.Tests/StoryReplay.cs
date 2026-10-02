extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class StoryReplay
{
    private static readonly string[] StoryFields = ["userCards", "userUnitEpisodeStatuses", "userSpecialEpisodeStatuses", "userReleaseConditions",
        "userCharacterMissionV2s", "userCharacterMissionV2Statuses", "userBeginnerMissionV2s", "userMissionStatuses", "userPanelMissions"];

    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "unitStories", "specialStories", "resourceBoxes", "cardEpisodes", "cards", "releaseConditions" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static TimeProvider Clock(string path)
    {
        var record = Read(path);
        return new StoryClock((record["response"]?["updatedResources"]?["now"] ?? record["before"]!["now"])!.GetValue<long>());
    }

    private sealed class StoryClock(long now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(now);
    }

    private static JsonObject Read(string path)
    {
        var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (record["operation"]?.GetValue<string>() != "story-read" || record["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要完整成功的官方剧情阅读记录。");
        return record;
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string path, string output)
    {
        var official = Read(path);
        var before = official["before"]!.DeepClone();
        BindUser(before);
        var state = store.Read(1)!;
        var fields = StoryFields.Concat(official["response"]?["updatedResources"]?.AsObject().Select(p => p.Key) ?? [])
            .Concat(["userGamedata", "userMaterials", "userPracticeTickets", "userVirtualCoin", "userJewel"])
            .ToHashSet(StringComparer.Ordinal);
        // 只导入剧情资源及本次刷新字段，不读取脱敏后的引继等账号凭证字段。
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => fields.Contains((string)m.Key)))
            if (before[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        try
        {
            await ScenarioRunner.Run(client, new() { Steps = [new()
            {
                Operation = "story-read",
                Args = JsonSerializer.Deserialize<Dictionary<string, string>>(official["args"]!.ToJsonString())!
            }] }, output);
        }
        finally
        {
            var localPath = Path.Combine(output, "001.json");
            if (File.Exists(localPath))
            {
                var local = JsonNode.Parse(File.ReadAllText(localPath))!.AsObject();
                JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official, local));
                foreach (var record in new[] { official, local })
                {
                    foreach (var side in new[] { "before", "after" }) record[side] = Select(record[side]);
                    if (record["response"] is JsonObject response && response.ContainsKey("updatedResources"))
                        response["updatedResources"] = Select(response["updatedResources"]);
                }
                JsonFiles.Write(Path.Combine(output, "story-compare.json"), ScenarioRunner.Compare(official, local));
            }
        }
    }

    private static JsonObject? Select(JsonNode? state) => state == null ? null :
        new JsonObject(StoryFields.Select(field => KeyValuePair.Create(field, state[field]?.DeepClone())));

    private static void BindUser(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var field in obj.ToArray())
                if (field.Key == "userId") obj[field.Key] = 1;
                else BindUser(field.Value);
        else if (node is JsonArray array)
            foreach (var item in array) BindUser(item);
    }
}
