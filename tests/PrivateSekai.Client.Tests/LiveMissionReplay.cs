extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class LiveMissionReplay
{
    private static readonly string[] Fields = ["userGamedata", "userLiveMissions", "userMissionStatuses"];

    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "liveMissions", "beginnerMissionV2s", "resourceBoxes" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static async Task RunBeginnerRepeat(ProtocolClient client, MemoryUserStore store, string path, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        Directory.CreateDirectory(output);
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (official["operation"]?.GetValue<string>() != "beginner-mission-receive" ||
            official["failurePhase"]?.GetValue<string>() != "request" || official["lastHttpStatus"]?.GetValue<int>() != 409)
            throw new InvalidOperationException("需要新手任务重复领取的官方 409 记录。");
        string[] fields = ["userMaterials", "userMissionStatuses", "userBeginnerMissionV2s"];
        var state = store.Read(1)!;
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => fields.Contains((string)m.Key)))
            if (official["before"]![(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        var before = JsonSerializer.SerializeToNode(store.Read(1)!.Data, DumpJson.Options);
        await client.Send(new() { Operation = "system" });
        client.CaptureTo(Path.Combine(output, "http"));
        var rejected = false;
        try
        {
            await client.Send(new() { Operation = "beginner-mission-receive", Body = official["request"]!.DeepClone().AsObject() });
        }
        catch (ClientFailure) { rejected = true; }
        var responseDifferences = Comparison.Diff(official["lastResponse"], client.LastResponse);
        var stateDifferences = Comparison.Diff(before, JsonSerializer.SerializeToNode(store.Read(1)!.Data, DumpJson.Options));
        JsonFiles.Write(Path.Combine(output, "beginner-repeat-compare.json"), new
        {
            rejected, expectedStatus = 409, actualStatus = client.LastHttpStatus, responseDifferences, stateDifferences
        });
        if (!rejected || client.LastHttpStatus != 409 || responseDifferences.Count != 0 || stateDifferences.Count != 0)
            throw new InvalidOperationException("新手任务重复领奖与官方拒绝样本不一致。");
        Console.WriteLine("新手任务重复领奖 HTTP 状态和错误正文与官方一致，本地用户状态无变化。");
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string path, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (official["operation"]?.GetValue<string>() != "live-mission-receive" || official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的 Live 任务领奖记录。");
        var state = store.Read(1)!;
        var before = official["before"]!.DeepClone();
        foreach (var field in Fields)
        {
            if (before[field] is JsonObject obj && obj["userId"] != null) obj["userId"] = 1;
            foreach (var item in before[field] as JsonArray ?? [])
                if (item?["userId"] != null) item["userId"] = 1;
        }
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => Fields.Contains((string)m.Key)))
            if (before[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = "live-mission-receive", Body = official["request"]!.DeepClone().AsObject()
        }] }, output);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official, local));
        foreach (var record in new[] { official, local })
        {
            foreach (var side in new[] { "before", "after" }) record[side] = Select(record[side]);
            record["response"]!["updatedResources"] = Select(record["response"]!["updatedResources"]);
        }
        var report = ScenarioRunner.Compare(official, local);
        JsonFiles.Write(Path.Combine(output, "live-mission-compare.json"), report);
        if (!report["complete"]!.GetValue<bool>() ||
            new[] { "httpStatusDifferences", "baselineDifferences", "responseDifferences", "deltaDifferences" }
                .Any(k => report[k]!.AsArray().Count != 0))
            throw new InvalidOperationException("Live 任务领奖存在差异，见重放报告。");
        Console.WriteLine("Live 任务领奖的金币、任务进度、任务状态和奖励响应对拍通过；其他奖励种类及背景字段未覆盖。");
    }

    private static JsonObject Select(JsonNode? source)
    {
        var selected = new JsonObject(Fields.Where(f => source?.AsObject().ContainsKey(f) == true)
            .Select(f => KeyValuePair.Create(f, source![f]?.DeepClone())));
        if (selected["userGamedata"] is JsonObject data)
            selected["userGamedata"] = new JsonObject { ["coin"] = data["coin"]?.DeepClone() };
        return selected;
    }
}
