extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class CharacterMissionReplay
{
    private static readonly string[] Fields = ["userCharacters", "userCharacterMissionV2s", "userCharacterMissionV2Statuses",
        "userChargedCurrency", "userMaterials", "userHonors", "userHonorBackgrounds", "userHonorWords", "userProfile", "userProfileHonors"];

    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "characterMissionV2s", "characterMissionV2ParameterGroups", "characterRanks", "levels", "resourceBoxes" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string path, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var operation = official["operation"]?.GetValue<string>();
        if (operation is not ("character-mission-receive" or "character-mission-receive-all") || official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的角色任务领奖记录。");
        var state = store.Read(1)!;
        var baseline = official["before"]!.DeepClone();
        foreach (var field in Fields)
        {
            if (baseline[field] is JsonArray rows)
                foreach (var row in rows)
                    if (row?["userId"] != null) row["userId"] = 1;
            if (baseline[field] is JsonObject obj && obj["userId"] != null) obj["userId"] = 1;
        }
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => Fields.Contains((string)m.Key)))
            if (baseline[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = operation,
            Args = JsonSerializer.Deserialize<Dictionary<string, string>>(official["args"]!.ToJsonString())!
        }] }, output);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official, local));
        foreach (var record in new[] { official, local })
        {
            foreach (var side in new[] { "before", "after" }) record[side] = Select(record[side]);
            record["response"]!["updatedResources"] = Select(record["response"]!["updatedResources"]);
        }
        var report = ScenarioRunner.Compare(official, local);
        JsonFiles.Write(Path.Combine(output, "character-mission-compare.json"), report);
        if (!report["complete"]!.GetValue<bool>() ||
            new[] { "httpStatusDifferences", "baselineDifferences", "responseDifferences", "deltaDifferences" }.Any(k => report[k]!.AsArray().Count != 0))
            throw new InvalidOperationException("角色任务领奖与官方样本存在差异。");
        Console.WriteLine("角色任务、经验与等级奖励 HTTP 对拍通过，其他背景字段未覆盖。");
    }

    private static JsonObject Select(JsonNode? source) => new(Fields.Where(f => source?.AsObject().ContainsKey(f) == true)
        .Select(f => KeyValuePair.Create(f, source![f]?.DeepClone())));
}
