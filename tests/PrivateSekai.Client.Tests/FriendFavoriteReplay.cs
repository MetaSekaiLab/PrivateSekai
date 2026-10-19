extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

internal static class FriendFavoriteReplay
{
    private static readonly string[] StateFields = ["userFriends", "userBeginnerMissionV2s", "userMissionStatuses"];

    public static TimeProvider Clock(string path)
    {
        var fixture = Load(path);
        var record = Load(fixture["record"]!.GetValue<string>());
        return new SampleClock(record["response"]?["updatedResources"]?["now"]?.GetValue<long>()
            ?? record["before"]!["now"]!.GetValue<long>());
    }

    public static async Task Run(ProtocolClient client, ProtocolClient readback, MemoryUserStore store, string path, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        var fixture = Load(path);
        var official = Load(fixture["record"]!.GetValue<string>());
        if (official["operation"]?.GetValue<string>() != "friend-favorite-save" || official["before"] == null)
            throw new InvalidOperationException("需要包含写前状态的好友收藏样本。");
        var rejected = official["status"]?.GetValue<string>() == "stopped";
        if (rejected && (official["failurePhase"]?.GetValue<string>() != "request" || official["lastHttpStatus"] == null))
            throw new InvalidOperationException("拒绝样本须已收到明确 HTTP 响应。");
        var expectedStatus = (rejected ? official["lastHttpStatus"] : official["httpStatus"])!.GetValue<int>();
        var owner = fixture["callerId"]!.GetValue<long>();
        var peers = official["before"]!["userFriends"]?.AsArray()
            .Select(friend => friend!["opponentUserId"]!.GetValue<long>()).Distinct().ToArray() ?? [];
        var ids = peers.Select((id, index) => (id, mapped: 1001L + index)).ToDictionary(pair => pair.id, pair => pair.mapped);
        long Map(long id) => id == owner ? 1 : ids.GetValueOrDefault(id, id);
        void Remap(JsonNode? node)
        {
            if (node is JsonArray array) foreach (var child in array) Remap(child);
            if (node is not JsonObject obj) return;
            foreach (var key in obj.Select(pair => pair.Key).ToArray())
            {
                if (key is "userId" or "friendId" or "targetUserId") obj[key] = 1;
                else if (key == "opponentUserId") obj[key] = Map(obj[key]!.GetValue<long>());
                else if (key == "favoriteUserIds" && obj[key] is JsonArray favorites)
                    obj[key] = new JsonArray(favorites.Select(id => (JsonNode?)JsonValue.Create(Map(id!.GetValue<long>()))).ToArray());
                else Remap(obj[key]);
            }
        }
        var before = official["before"]!.DeepClone();
        Remap(before);
        var import = before.DeepClone().AsObject();
        foreach (var key in import.Select(pair => pair.Key).Where(Redactor.IsSensitive).ToArray()) import.Remove(key);
        var data = JsonSerializer.Deserialize<SuiteUser>(import.ToJsonString(), DumpJson.Options)!;
        data.userRegistration.userId = 1;
        store.Save(1, new UserState { Data = data });
        var body = fixture["requestBody"]!.DeepClone().AsObject();
        if (!JsonNode.DeepEquals(client.Redactor.Clean(body), official["request"]))
            throw new InvalidOperationException("本地请求与官方样本结构不一致。");
        Remap(body);
        client.CaptureTo(Path.Combine(output, "http"));
        await client.Send(new() { Operation = "system" });
        var baseline = await client.Send(new() { Operation = "suite-friends" });
        JsonNode? response;
        try { response = await client.Send(new() { Operation = "friend-favorite-save", Body = body }); }
        catch (ClientFailure) { response = client.LastResponse?.DeepClone(); }
        var status = client.LastHttpStatus;
        var expectedResponse = (rejected ? official["lastResponse"] : official["response"])!.DeepClone();
        Remap(expectedResponse);
        var expectedAfter = rejected
            ? Load(fixture["readback"]!.GetValue<string>())["response"]!.DeepClone()
            : official["after"]!.DeepClone();
        Remap(expectedAfter);
        readback.CaptureTo(Path.Combine(output, "readback"));
        await readback.Send(new() { Operation = "system" });
        var after = rejected ? await readback.Send(new() { Operation = "suite-friends" }) : await readback.Suite();
        var fields = rejected ? new[] { "userFriends" } : StateFields;
        var report = new JsonObject
        {
            ["httpStatusMatches"] = status == expectedStatus,
            ["baselineDifferences"] = Differences(client.Redactor.Clean(Select(before, ["userFriends"])), client.Redactor.Clean(Select(baseline, ["userFriends"]))),
            ["responseDifferences"] = Differences(client.Redactor.Clean(expectedResponse), client.Redactor.Clean(response)),
            ["readbackDifferences"] = Differences(client.Redactor.Clean(Select(expectedAfter, fields)), readback.Redactor.Clean(Select(after, fields))),
            ["friendDeltaDifferences"] = Differences(
                Comparison.DeltaView(Comparison.Diff(Select(before, ["userFriends"]), Select(expectedAfter, ["userFriends"]))),
                Comparison.DeltaView(Comparison.Diff(Select(baseline, ["userFriends"]), Select(after, ["userFriends"]))))
        };
        JsonFiles.Write(Path.Combine(output, "friend-favorite-compare.json"), report);
        if (!report["httpStatusMatches"]!.GetValue<bool>() || report.Where(pair => pair.Key.EndsWith("Differences", StringComparison.Ordinal))
            .Any(pair => pair.Value!.AsArray().Count != 0))
            throw new InvalidOperationException("好友收藏的 HTTP 响应、状态或独立回读存在差异，见报告。");
        Console.WriteLine("好友收藏完整响应、列表增量及关联任务独立回读对拍通过。");
    }

    private static JsonArray Differences(JsonNode? expected, JsonNode? actual) =>
        JsonSerializer.SerializeToNode(Comparison.Diff(Comparison.Normalize(expected), Comparison.Normalize(actual)), JsonFiles.Options)!.AsArray();

    private static JsonObject Select(JsonNode? source, string[] fields) => new(fields
        .Where(field => source?.AsObject().ContainsKey(field) == true)
        .Select(field => KeyValuePair.Create(field, source![field]?.DeepClone())));

    private static JsonObject Load(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private sealed class SampleClock(long timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
    }
}
