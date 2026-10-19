extern alias game;

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

internal static class AvatarMotionFavoriteReplay
{
    private static readonly string[] Fields = ["userAvatarMotionFavorites", "userAvatarMotions", "userBeginnerMissionV2s", "userMissionStatuses"];

    public static TimeProvider Clock(string manifest) => new ReplayClock(Timestamp(Load(Load(manifest)["records"]![0]!["record"]!.GetValue<string>())));

    public static async Task Run(TargetConfiguration config, MemoryUserStore store, TimeProvider time,
        string directory, string manifest, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        Directory.CreateDirectory(output);
        var results = new JsonArray();
        foreach (var fixture in Load(manifest)["records"]!.AsArray())
        {
            var official = Load(fixture!["record"]!.GetValue<string>());
            var rejected = official["status"]?.GetValue<string>() == "stopped";
            if (official["operation"]?.GetValue<string>() != "avatar-motion-favorite-save" || official["before"] == null ||
                (rejected && (official["failurePhase"]?.GetValue<string>() != "request" || official["lastHttpStatus"] == null)))
                throw new InvalidOperationException("需要已收到明确业务响应及写前状态的动作收藏样本。");
            ((ReplayClock)time).Timestamp = Timestamp(official);
            var before = official["before"]!.DeepClone();
            Remap(before);
            var import = before.DeepClone().AsObject();
            foreach (var key in import.Select(pair => pair.Key).Where(Redactor.IsSensitive).ToArray()) import.Remove(key);
            var data = JsonSerializer.Deserialize<SuiteUser>(import.ToJsonString(), DumpJson.Options)!;
            data.userRegistration.userId = 1;
            store.Save(1, new UserState { Data = data });
            using var client = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            var body = fixture["requestBody"]!.DeepClone().AsObject();
            if (!JsonNode.DeepEquals(client.Redactor.Clean(body), official["request"]))
                throw new InvalidOperationException("请求与官方样本不一致。");
            await client.Send(new() { Operation = "system" });
            JsonNode? response;
            try { response = await client.Send(new() { Operation = "avatar-motion-favorite-save", Body = body }); }
            catch (ClientFailure) { response = client.LastResponse?.DeepClone(); }
            var status = client.LastHttpStatus;
            var expected = (rejected ? official["lastResponse"] : official["response"])!.DeepClone();
            var expectedAfter = rejected ? Load(fixture["readback"]!.GetValue<string>())["response"]!.DeepClone() : official["after"]!.DeepClone();
            Remap(expected);
            Remap(expectedAfter);
            using var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await readback.Send(new() { Operation = "system" });
            var after = await readback.Suite();
            var report = new JsonObject
            {
                ["label"] = fixture["label"]!.DeepClone(),
                ["httpStatusMatches"] = status == (rejected ? official["lastHttpStatus"] : official["httpStatus"])!.GetValue<int>(),
                ["responseDifferences"] = Differences(client.Redactor.Clean(expected), client.Redactor.Clean(response)),
                ["readbackDifferences"] = Differences(client.Redactor.Clean(Select(expectedAfter)), client.Redactor.Clean(Select(after))),
                ["deltaDifferences"] = Differences(
                    Comparison.DeltaView(Comparison.Diff(client.Redactor.Clean(Select(before)), client.Redactor.Clean(Select(expectedAfter)))),
                    Comparison.DeltaView(Comparison.Diff(client.Redactor.Clean(Select(before)), client.Redactor.Clean(Select(after))))),
                ["expectedResponse"] = client.Redactor.Clean(expected),
                ["actualResponse"] = client.Redactor.Clean(response)
            };
            results.Add(report);
            JsonFiles.Write(Path.Combine(output, "avatar-motion-favorite-compare.json"), results);
            if (!report["httpStatusMatches"]!.GetValue<bool>() || report.Where(pair => pair.Key.EndsWith("Differences", StringComparison.Ordinal))
                .Any(pair => pair.Value!.AsArray().Count != 0))
                throw new InvalidOperationException("动作快捷栏完整响应或独立回读有差异，见报告。");
            Console.WriteLine($"动作快捷栏 {fixture["label"]!.GetValue<string>()} 对拍通过。");
        }
        Console.WriteLine($"动作快捷栏共 {results.Count} 组完整响应及独立回读对拍通过。");
    }

    private static void Remap(JsonNode? node)
    {
        if (node is JsonArray array) foreach (var child in array) Remap(child);
        if (node is not JsonObject obj) return;
        foreach (var key in obj.Select(pair => pair.Key).ToArray())
            if (key == "userId") obj[key] = 1;
            else Remap(obj[key]);
    }

    private static JsonObject Select(JsonNode? source) => new(Fields.Where(field => source?.AsObject().ContainsKey(field) == true)
        .Select(field => KeyValuePair.Create(field, source![field]?.DeepClone())));

    private static JsonArray Differences(JsonNode? expected, JsonNode? actual) =>
        JsonSerializer.SerializeToNode(Comparison.Diff(Comparison.Normalize(expected), Comparison.Normalize(actual)), JsonFiles.Options)!.AsArray();

    private static long Timestamp(JsonNode record) => record["response"]?["updatedResources"]?["now"]?.GetValue<long>() ?? record["before"]!["now"]!.GetValue<long>();

    private static JsonObject Load(string path)
    {
        using var file = File.OpenRead(path);
        if (!path.EndsWith(".gz", StringComparison.Ordinal)) return JsonNode.Parse(file)!.AsObject();
        using var decompressed = new GZipStream(file, CompressionMode.Decompress);
        return JsonNode.Parse(decompressed)!.AsObject();
    }

    private sealed class ReplayClock(long timestamp) : TimeProvider
    {
        public long Timestamp { get; set; } = timestamp;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp);
    }
}
