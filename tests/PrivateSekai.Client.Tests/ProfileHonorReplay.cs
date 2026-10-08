extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class ProfileHonorReplay
{
    private static readonly string[] Fields = ["userProfileHonors", "userHonors", "userHonorBackgrounds", "userHonorWords"];

    public static async Task Run(ProtocolClient client, ProtocolClient readback, MemoryUserStore store, string path, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (official["operation"]?.GetValue<string>() != "profile-honor-save")
            throw new InvalidOperationException("需要称号设置记录。");
        var rejected = official["status"]?.GetValue<string>() == "stopped";
        if (rejected ? official["failurePhase"]?.GetValue<string>() != "request" || official["lastHttpStatus"]?.GetValue<int>() != 409
            : official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要已完成或已收到 409 的记录。");
        var state = store.Read(1)!;
        var baseline = official["before"]!.DeepClone();
        foreach (var honor in baseline["userHonors"]?.AsArray() ?? []) honor!["userId"] = 1;
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => Fields.Contains((string)m.Key)))
            member.Set(state.Data, baseline[(string)member.Key] is { } value
                ? JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options) : null);
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        try
        {
            await ScenarioRunner.Run(client, new() { Steps = [new()
            {
                Operation = "profile-honor-save", Body = official["request"]!.DeepClone().AsObject()
            }] }, output);
        }
        catch (ClientFailure) when (rejected && client.LastHttpStatus == 409) { }
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        readback.CaptureTo(Path.Combine(output, "readback"));
        await readback.Send(new() { Operation = "system" });
        var after = await readback.Suite();
        foreach (var record in new[] { official, local })
        {
            record["before"] = Select(record["before"]);
            if (!rejected)
            {
                record["after"] = Select(record["after"]);
                record["response"]!["updatedResources"] = Select(record["response"]!["updatedResources"]);
            }
        }
        var report = ScenarioRunner.Compare(official, local);
        var readbackDifferences = Comparison.Diff(Comparison.Normalize(rejected ? local["before"] : official["after"]),
            Comparison.Normalize(readback.Redactor.Clean(Select(after))));
        report["readbackDifferences"] = JsonSerializer.SerializeToNode(readbackDifferences, JsonFiles.Options);
        JsonFiles.Write(Path.Combine(output, "profile-honor-compare.json"), report);
        var keys = rejected ? new[] { "httpStatusDifferences", "baselineDifferences", "responseDifferences" }
            : new[] { "httpStatusDifferences", "baselineDifferences", "responseDifferences", "deltaDifferences" };
        if (readbackDifferences.Count != 0 || (!rejected && !report["complete"]!.GetValue<bool>()) ||
            keys.Any(k => report[k]!.AsArray().Count != 0))
            throw new InvalidOperationException("称号 HTTP 对拍存在差异，见报告。");
        Console.WriteLine("称号装备、持有列表、响应及独立回读专项 HTTP 对拍通过。");
    }

    private static JsonObject Select(JsonNode? source) => new(Fields
        .Where(f => source?.AsObject().ContainsKey(f) == true)
        .Select(f => KeyValuePair.Create(f, source![f]?.DeepClone())));
}
