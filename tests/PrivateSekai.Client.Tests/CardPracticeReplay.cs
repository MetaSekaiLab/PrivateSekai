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
        "userMaterials", "userSkillPracticeTickets", "userCharacterMissionV2s", "userCharacterMissionV2Statuses", "userReleaseConditions"];

    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "cards", "cardRarities", "levels", "practiceTickets", "beginnerMissionV2s", "cardEpisodes", "releaseConditions",
                     "characterMissionV2s", "characterMissionV2ParameterGroups", "resourceBoxes", "cardSkillCosts", "skillPracticeTickets", "gameCharacters", "facilities" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string path, string output,
        string? readbackPath = null, Func<ProtocolClient>? createReadbackClient = null)
    {
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var operation = official["operation"]?.GetValue<string>();
        var rejected = official["status"]?.GetValue<string>() == "stopped" &&
            official["failurePhase"]?.GetValue<string>() == "request" && official["lastHttpStatus"]?.GetValue<int>() == 409;
        if (operation is not ("card-practice" or "special-training" or "skill-material" or "skill-ticket") ||
            (official["status"]?.GetValue<string>() != "completed" && !rejected))
            throw new InvalidOperationException("需要成功或明确返回 409 的卡牌养成记录。");
        if (readbackPath != null)
        {
            var readback = JsonNode.Parse(File.ReadAllText(readbackPath))!.AsObject();
            if (readback["operation"]?.GetValue<string>() != "suite" || readback["status"]?.GetValue<string>() != "completed")
                throw new InvalidOperationException("回读记录必须为同一测试账号后续成功的 suite 请求。");
            official["after"] = readback["response"]!.DeepClone();
        }
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
        try
        {
            await ScenarioRunner.Run(client, new() { Steps = [new()
            {
                Operation = operation,
                Args = JsonSerializer.Deserialize<Dictionary<string, string>>(official["args"]!.ToJsonString())!,
                Body = official["request"]!.DeepClone().AsObject()
            }] }, output);
        }
        catch (ClientFailure) when (client.LastHttpStatus == 409)
        {
            // 保留拒绝记录参与比较，不把失败请求视为成功。
        }
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        if (local["status"]?.GetValue<string>() == "stopped" && createReadbackClient != null)
        {
            using var readbackClient = createReadbackClient();
            var readbackDirectory = Path.Combine(output, "rejection-readback");
            await ScenarioRunner.Run(readbackClient, new() { Steps = [new() { Operation = "system" }, new() { Operation = "suite" }] }, readbackDirectory);
            var readback = JsonNode.Parse(File.ReadAllText(Path.Combine(readbackDirectory, "002.json")))!;
            local["after"] = readback["response"]!.DeepClone();
            JsonFiles.Write(Path.Combine(output, "001.json"), local);
        }
        JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official, local));
        foreach (var record in new[] { official, local })
        {
            foreach (var side in new[] { "before", "after" })
                if (record[side] != null) record[side] = Select(record[side]);
            if (record["response"]?["updatedResources"] is { } resources)
                record["response"]!["updatedResources"] = Select(resources);
        }
        JsonFiles.Write(Path.Combine(output, operation + "-compare.json"), ScenarioRunner.Compare(official, local));
    }

    private static JsonObject Select(JsonNode? source) => new(Fields.Select(field => KeyValuePair.Create(field, source?[field]?.DeepClone())));
}
