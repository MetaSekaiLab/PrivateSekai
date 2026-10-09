extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class LiveStartReplay
{
    private static readonly string[] Fields = ["userMusics", "userMusicVocals", "userMusicResults", "userHonorMissions",
        "userMissionStatuses", "userMaterials", "userAutoLive"];

    public static TimeProvider Clock(string path) => new ReplayClock(JsonNode.Parse(File.ReadAllText(path))!["before"]!["now"]!.GetValue<long>());
    private sealed class ReplayClock(long now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(now);
    }

    public static async Task Run(ProtocolClient client, ProtocolClient readback, MemoryUserStore store,
        string path, string officialReadbackPath, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var officialReadback = JsonNode.Parse(File.ReadAllText(officialReadbackPath))!;
        if (official["operation"]?.GetValue<string>() != "live-start" || official["failurePhase"]?.GetValue<string>() != "request" ||
            official["lastHttpStatus"]?.GetValue<int>() is not (400 or 404) ||
            officialReadback["operation"]?.GetValue<string>() != "suite" || officialReadback["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要被拒绝的开局记录及随后的独立 Suite 回读。");
        Seed(store, official);
        await client.Send(new() { Operation = "system" });
        await client.Suite();
        var state = store.Read(1)!;
        var dataBefore = DumpSerializer.Serialize(state.Data);
        var sessionsBefore = state.Private.UserLiveSessions.Count;
        try
        {
            await ScenarioRunner.Run(client, new() { Steps = [new()
            {
                Operation = "live-start", Body = official["request"]!.DeepClone().AsObject()
            }] }, output);
        }
        catch (ClientFailure) when (client.LastHttpStatus == official["lastHttpStatus"]!.GetValue<int>()) { }
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        var saved = store.Read(1)!;
        if (!dataBefore.SequenceEqual(DumpSerializer.Serialize(saved.Data)) || saved.Private.UserLiveSessions.Count != sessionsBefore)
            throw new InvalidOperationException("被拒开局改变了本地用户状态或私有会话。");
        await readback.Send(new() { Operation = "system" });
        var after = readback.Redactor.Clean(await readback.Suite());
        var officialChanges = Comparison.Diff(Comparison.Normalize(Select(official["before"])),
            Comparison.Normalize(Select(officialReadback["response"])));
        var localChanges = Comparison.Diff(Comparison.Normalize(Select(local["before"])), Comparison.Normalize(Select(after)));
        foreach (var record in new[] { official, local }) record["before"] = Select(record["before"]);
        var report = ScenarioRunner.Compare(official, local);
        report["officialReadbackDifferences"] = JsonSerializer.SerializeToNode(officialChanges, JsonFiles.Options);
        report["localReadbackDifferences"] = JsonSerializer.SerializeToNode(localChanges, JsonFiles.Options);
        JsonFiles.Write(Path.Combine(output, "live-start-rejection-compare.json"), report);
        if (officialChanges.Count != 0 || localChanges.Count != 0 ||
            new[] { "httpStatusDifferences", "baselineDifferences", "responseDifferences" }.Any(k => report[k]!.AsArray().Count != 0))
            throw new InvalidOperationException("开局拒绝响应或独立回读与官方样本不一致。");
        Console.WriteLine("开局拒绝状态、错误响应、独立回读和本地会话不变检查通过。");
    }

    private static void Seed(MemoryUserStore store, JsonObject official)
    {
        var state = store.Read(1)!;
        var baseline = official["before"]!.DeepClone();
        foreach (var status in baseline["userMissionStatuses"]?.AsArray() ?? [])
            if (status?["userId"] != null) status["userId"] = 1;
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => Fields.Contains((string)m.Key) || (string)m.Key == "userBoost"))
            if (baseline[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
    }

    private static JsonObject Select(JsonNode? source)
    {
        var result = new JsonObject(Fields.Select(f => KeyValuePair.Create(f, source?[f]?.DeepClone())));
        result["boostCount"] = source?["userBoost"]?["current"]?.DeepClone();
        return result;
    }

    public static async Task RunAccepted(ProtocolClient client, ProtocolClient readback, MemoryUserStore store,
        string path, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (official["operation"]?.GetValue<string>() != "live-start" || official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的官方普通 Live 开局记录。");
        Seed(store, official);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = "live-start", Body = official["request"]!.DeepClone().AsObject()
        }] }, output);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        var request = JsonSerializer.Deserialize<UserLiveRequest>(official["request"]!.ToJsonString(), DumpJson.Options)!;
        var session = store.Read(1)!.Private.UserLiveSessions[local["response"]!["userLiveId"]!.GetValue<string>()];
        if (session.MusicId != request.musicId || session.MusicDifficultyId != request.musicDifficultyId ||
            session.MusicVocalId != request.musicVocalId || session.MusicCategoryName != request.musicCategoryName ||
            session.DeckId != request.deckId || session.BoostCount != request.boostCount || session.IsAuto != request.isAuto ||
            session.CustomMusicScoreId != request.customMusicScoreId)
            throw new InvalidOperationException("开局会话未保留请求字段。");
        await readback.Send(new() { Operation = "system" });
        var after = readback.Redactor.Clean(await readback.Suite());
        var differences = Comparison.Diff(Comparison.Normalize(Select(official["after"])), Comparison.Normalize(Select(after)));
        JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official, local));
        foreach (var record in new[] { official, local })
        {
            foreach (var side in new[] { "before", "after" }) record[side] = Select(record[side]);
            if (string.IsNullOrEmpty(record["response"]?["userLiveId"]?.GetValue<string>()))
                throw new InvalidOperationException("成功开局缺少 Live ID。");
            record["response"] = new JsonObject { ["isInBreakTime"] = record["response"]!["isInBreakTime"]!.DeepClone() };
        }
        var report = ScenarioRunner.Compare(official, local);
        report["readbackDifferences"] = JsonSerializer.SerializeToNode(differences, JsonFiles.Options);
        JsonFiles.Write(Path.Combine(output, "live-start-acceptance-compare.json"), report);
        if (!report["complete"]!.GetValue<bool>() || differences.Count != 0 ||
            new[] { "httpStatusDifferences", "baselineDifferences", "responseDifferences", "deltaDifferences" }.Any(k => report[k]!.AsArray().Count != 0))
            throw new InvalidOperationException("开局受理状态或独立回读与官方样本不同。");
        Console.WriteLine("开局受理、Live ID、请求字段保存及选定状态独立回读通过；技能顺序和切入不在此断言范围。");
    }
}
