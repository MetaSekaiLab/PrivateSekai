extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

internal static class SuiteSelectionReplay
{
    private static readonly string[] StateFields = ["userCards", "userChargedCurrency", "userMaterials", "userGachaTickets",
        "userReleaseConditions", "userEventEpisodeStatuses", "userMissionStatuses", "userLiveMissions"];

    public static TimeProvider Clock() => new ReplayClock();

    public static async Task Run(TargetConfiguration config, MemoryUserStore store, TimeProvider time,
        string directory, string manifest, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        Directory.CreateDirectory(output);
        var fields = DumpContract.For(typeof(SuiteUser)).Members.ToDictionary(m => (string)m.Key, StringComparer.Ordinal);
        var results = new JsonArray();
        var baselines = new Dictionary<(string Before, string After), Baseline>();
        var redactor = new Redactor();
        foreach (var fixture in Load(manifest)["records"]!.AsArray())
        {
            var official = Load(fixture!["record"]!.GetValue<string>());
            var name = fixture["name"]?.GetValue<string>();
            var native = fixture["native"]?.GetValue<bool>() == true;
            var operation = official["operation"]!.GetValue<string>();
            if (official["status"]!.GetValue<string>() != "completed" || official["httpStatus"]!.GetValue<int>() != 200 ||
                (native ? operation != "suite-story-release" : operation != (name == null ? "probe-no-name" : "probe-" + name)))
                throw new InvalidOperationException("需要对应名称的官方成功Suite查询记录。");
            var baselineKey = (fixture["before"]!.GetValue<string>(), fixture["after"]!.GetValue<string>());
            if (!baselines.TryGetValue(baselineKey, out var baseline))
            {
                var before = Load(baselineKey.Item1)["response"]!.AsObject();
                var expectedAfter = Load(baselineKey.Item2)["response"]!.AsObject();
                Remap(before);
                Remap(expectedAfter);
                var import = before.DeepClone().AsObject();
                foreach (var key in import.Select(p => p.Key).Where(Redactor.IsSensitive).ToArray()) import.Remove(key);
                var imported = JsonSerializer.Deserialize<SuiteUser>(import.ToJsonString(), DumpJson.Options)!;
                foreach (var (key, value) in before)
                    if (Redactor.IsSensitive(key) && fields.TryGetValue(key, out var field) && value != null)
                        field.Set(imported, field.Type.IsArray ? Array.CreateInstance(field.Type.GetElementType()!, 0) : Activator.CreateInstance(field.Type));
                baseline = new(imported, DumpSerializer.Serialize(imported), expectedAfter,
                    Differences(redactor.Clean(Select(before)), redactor.Clean(Select(expectedAfter))));
                baselines.Add(baselineKey, baseline);
            }
            var expected = official["response"]!.DeepClone().AsObject();
            if (expected.Any(p => !fields.ContainsKey(p.Key)))
                throw new InvalidOperationException("样本含当前SuiteUser模型未映射的响应字段。");
            ((ReplayClock)time).Timestamp = expected["now"]!.GetValue<long>();
            Remap(expected);
            store.Save(1, new UserState { Data = baseline.Data });
            using var client = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await client.Send(new() { Operation = "system" });
            ScenarioStep step;
            if (native) step = new() { Operation = "suite-story-release" };
            else if (name != null) step = new() { Operation = "suite-select", Args = new() { ["resourceNames"] = name } };
            else
            {
                ((IDictionary<string, OperationDefinition>)Operations.All).TryAdd("suite-replay-common",
                    new("GET", "/api/suite/user/{userId}?isForceAllReload=false", null, "now", false));
                step = new() { Operation = "suite-replay-common" };
            }
            var response = await client.Send(step);
            var status = client.LastHttpStatus;
            var unchanged = baseline.Snapshot.SequenceEqual(DumpSerializer.Serialize(store.Read(1)!.Data));
            var readbackPerformed = baseline.ReadbackDifferences == null;
            if (readbackPerformed)
            {
                using var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
                await readback.Send(new() { Operation = "system" });
                var after = await readback.Suite();
                baseline.ReadbackDifferences = Differences(redactor.Clean(Select(baseline.ExpectedAfter)), redactor.Clean(Select(after)));
            }
            var expectedClean = client.Redactor.Clean(expected);
            var responseClean = client.Redactor.Clean(response);
            var report = new JsonObject
            {
                ["label"] = fixture["label"]!.DeepClone(), ["httpStatusMatches"] = status == 200,
                ["storedStateUnchanged"] = unchanged,
                ["independentReadbackPerformed"] = readbackPerformed,
                ["responseDifferences"] = Differences(expectedClean, responseClean),
                ["readbackDifferences"] = baseline.ReadbackDifferences!.DeepClone(),
                ["officialStateDifferences"] = baseline.OfficialDifferences.DeepClone()
            };
            results.Add(report);
            JsonFiles.Write(Path.Combine(output, "case-compare.json"), results);
            if (report["responseDifferences"]!.AsArray().Count > 0)
                JsonFiles.Write(Path.Combine(output, $"{results.Count:D3}-response.json"),
                    new JsonObject { ["expected"] = expectedClean, ["actual"] = responseClean });
            Console.WriteLine($"Suite回放 {results.Count}: {fixture["label"]!.GetValue<string>()}");
        }
        if (results.Any(r => !r!["httpStatusMatches"]!.GetValue<bool>() || !r["storedStateUnchanged"]!.GetValue<bool>() ||
            r.AsObject().Where(p => p.Key.EndsWith("Differences", StringComparison.Ordinal)).Any(p => p.Value!.AsArray().Count != 0)))
            throw new InvalidOperationException("Suite完整响应、存储隔离或独立回读存在差异，见报告。");
        Console.WriteLine($"Suite选择共 {results.Count} 组完整响应及八项状态独立回读对拍通过。");
    }

    private static JsonObject Select(JsonObject source) => new(StateFields.Where(source.ContainsKey)
        .Select(key => KeyValuePair.Create(key, source[key]?.DeepClone())));

    private static JsonArray Differences(JsonNode? expected, JsonNode? actual) =>
        JsonSerializer.SerializeToNode(Comparison.Diff(expected, actual), JsonFiles.Options)!.AsArray();

    private static void Remap(JsonNode? node)
    {
        if (node is JsonArray array) foreach (var child in array) Remap(child);
        if (node is not JsonObject obj) return;
        foreach (var key in obj.Select(p => p.Key).ToArray())
            if (key == "userId") obj[key] = 1;
            else Remap(obj[key]);
    }

    private static JsonObject Load(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private sealed record Baseline(SuiteUser Data, byte[] Snapshot, JsonObject ExpectedAfter, JsonArray OfficialDifferences)
    {
        public JsonArray? ReadbackDifferences { get; set; }
    }

    private sealed class ReplayClock : TimeProvider
    {
        public long Timestamp { get; set; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp);
    }
}
