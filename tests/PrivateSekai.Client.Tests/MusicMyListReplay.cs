extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class MusicMyListReplay
{
    private static readonly string[] Fields = ["userMyLists", "userMusics", "userMusicVocals"];

    public static async Task Run(ProtocolClient client, ProtocolClient readback, MemoryUserStore store, string path, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        // 名称在抓包中已脱敏，由本地场景提供测试名称，不修改原始样本。
        var fixture = JsonNode.Parse(File.ReadAllText(path))!;
        var official = JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, fixture["record"]!.GetValue<string>())))!.AsObject();
        var operation = official["operation"]!.GetValue<string>();
        if (operation is not ("music-my-list-save" or "music-my-list-reset"))
            throw new InvalidOperationException("需要 My List 操作记录。");
        var rejected = official["status"]!.GetValue<string>() == "stopped";
        if (rejected && (official["failurePhase"]!.GetValue<string>() != "request" ||
            official["lastHttpStatus"]!.GetValue<int>() is not (400 or 404)))
            throw new InvalidOperationException("需要已收到响应的 My List 记录。");
        var state = store.Read(1)!;
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => Fields.Contains((string)m.Key)))
            member.Set(state.Data, official["before"]![(string)member.Key] is { } value
                ? JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options) : null);
        foreach (var list in state.Data.userMusicMyList ?? [])
            list.name = fixture["beforeNames"]![list.listNo.ToString()]!.GetValue<string>();
        store.Save(1, state);
        var body = official["request"]?.DeepClone().AsObject();
        if (body != null) body["name"] = fixture["requestName"]!.GetValue<string>();
        await client.Send(new() { Operation = "system" });
        try
        {
            await ScenarioRunner.Run(client, new() { Steps = [new()
            {
                Operation = operation, Body = body,
                Args = JsonSerializer.Deserialize<Dictionary<string, string>>(official["args"]!.ToJsonString())!
            }] }, output);
        }
        catch (ClientFailure) when (rejected && client.LastHttpStatus == official["lastHttpStatus"]!.GetValue<int>()) { }
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        readback.CaptureTo(Path.Combine(output, "readback"));
        await readback.Send(new() { Operation = "system" });
        var after = await readback.Suite();
        foreach (var list in after["userMyLists"]?.AsArray() ?? [])
            if (list!["name"]!.GetValue<string>() != fixture["afterNames"]![list["listNo"]!.ToString()]!.GetValue<string>())
                throw new InvalidOperationException("My List 名称与场景预期不符。");
        foreach (var record in new[] { official, local })
        {
            record["before"] = Select(record["before"]);
            if (!rejected)
            {
                record["after"] = Select(record["after"]);
                record["response"]!["updateResources"] = Select(record["response"]!["updateResources"]);
            }
        }
        var report = ScenarioRunner.Compare(official, local);
        var unchanged = !rejected || Comparison.Diff(Comparison.Normalize(local["before"]),
            Comparison.Normalize(readback.Redactor.Clean(Select(after)))).Count == 0;
        report["rejectedStateUnchanged"] = unchanged;
        JsonFiles.Write(Path.Combine(output, "my-list-compare.json"), report);
        var keys = rejected ? new[] { "httpStatusDifferences", "baselineDifferences", "responseDifferences" }
            : new[] { "httpStatusDifferences", "baselineDifferences", "responseDifferences", "deltaDifferences" };
        if (!unchanged || (!rejected && !report["complete"]!.GetValue<bool>()) || keys.Any(k => report[k]!.AsArray().Count != 0))
            throw new InvalidOperationException("My List HTTP 对拍存在差异，见报告。");
        Console.WriteLine("My List 状态、名称、响应及音乐持有列表专项 HTTP 对拍通过。");
    }

    private static JsonObject Select(JsonNode? source) => new(Fields
        .Where(f => source?.AsObject().ContainsKey(f) == true)
        .Select(f => KeyValuePair.Create(f, source![f]?.DeepClone())));
}
