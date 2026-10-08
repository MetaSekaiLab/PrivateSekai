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
        var state = store.Read(1)!;
        var baseline = official["before"]!.DeepClone();
        foreach (var status in baseline["userMissionStatuses"]?.AsArray() ?? [])
            if (status?["userId"] != null) status["userId"] = 1;
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => Fields.Contains((string)m.Key) || (string)m.Key == "userBoost"))
            if (baseline[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await client.Suite();
        state = store.Read(1)!;
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

    private static JsonObject Select(JsonNode? source)
    {
        var result = new JsonObject(Fields.Select(f => KeyValuePair.Create(f, source?[f]?.DeepClone())));
        result["boostCount"] = source?["userBoost"]?["current"]?.DeepClone();
        return result;
    }
}
