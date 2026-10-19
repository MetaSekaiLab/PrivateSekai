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

internal static class UserCaseReplay
{
    public static readonly string[] MotionFields = ["userAvatarMotionFavorites", "userAvatarMotions", "userBeginnerMissionV2s", "userMissionStatuses"];
    public static readonly string[] VirtualShopFields = ["userVirtualShops", "userAvatarMotions", "userAvatarCostumes", "userAvatarAccessories",
        "userAvatarCoordinates", "userAvatarSkinColors", "userPenlights", "userAvatar", "userChargedCurrency", "userGamedata",
        "userBeginnerMissionV2s", "userMissionStatuses"];
    public static readonly string[] AvatarFields = [.. VirtualShopFields, "userAvatarMotionFavorites"];
    public static readonly string[] CostumeFixFields = ["userFixCostumes", "userCharacterCostume3ds", "userCostume3dStatuses",
        "userCostume3dShopItems", "userMaterials", "userChargedCurrency", "userGamedata", "userBeginnerMissionV2s",
        "userMissionStatuses", "userCharacterMissions", "userCharacterMissionStatuses"];
    public static readonly string[] StampUseFields = ["userHonorMissions", "userMissionStatuses", "userHonors", "userProfile", "userProfileHonors",
        "userStamps", "userStampFavorites", "userStampFavoriteTabs", "userMaterials", "userChargedCurrency", "userGamedata",
        "userBeginnerMissionV2s", "userCharacterMissions", "userCharacterMissionStatuses"];

    public static TimeProvider Clock(string manifest) => new ReplayClock(Timestamp(Load(Load(manifest)["records"]![0]!["record"]!.GetValue<string>())));

    public static async Task Run(TargetConfiguration config, MemoryUserStore store, TimeProvider time,
        string directory, string manifest, string output, string operation, string[] fields, string? secondaryOperation = null)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        Directory.CreateDirectory(output);
        var results = new JsonArray();
        foreach (var fixture in Load(manifest)["records"]!.AsArray())
        {
            var official = Load(fixture!["record"]!.GetValue<string>());
            var action = official["operation"]?.GetValue<string>();
            var rejected = official["status"]?.GetValue<string>() == "stopped";
            if ((action != operation && (secondaryOperation == null || action != secondaryOperation)) || official["before"] == null ||
                (rejected && (official["failurePhase"]?.GetValue<string>() != "request" || official["lastHttpStatus"] == null)))
                throw new InvalidOperationException("需要已收到明确业务响应及写前状态的对应操作样本。");
            ((ReplayClock)time).Timestamp = Timestamp(official);
            var before = official["before"]!.DeepClone();
            Remap(before);
            var import = before.DeepClone().AsObject();
            foreach (var key in import.Select(pair => pair.Key).Where(Redactor.IsSensitive).ToArray()) import.Remove(key);
            var data = JsonSerializer.Deserialize<SuiteUser>(import.ToJsonString(), DumpJson.Options)!;
            data.userRegistration.userId = 1;
            store.Save(1, new UserState { Data = data });
            using var client = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            var body = fixture["requestBody"]?.DeepClone().AsObject();
            if (!JsonNode.DeepEquals(client.Redactor.Clean(body), official["request"]))
                throw new InvalidOperationException("请求与官方样本不一致。");
            foreach (var key in new[] { "args", "query" })
                if (!JsonNode.DeepEquals(fixture[key] ?? new JsonObject(), official[key] ?? new JsonObject()))
                    throw new InvalidOperationException("路径或查询参数与官方样本不一致。");
            await client.Send(new() { Operation = "system" });
            JsonNode? response;
            Remap(body);
            try { response = await client.Send(new() { Operation = action!, Body = body, Args = Map(fixture["args"]), Query = Map(fixture["query"]) }); }
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
                ["readbackDifferences"] = Differences(client.Redactor.Clean(Select(expectedAfter, fields)), client.Redactor.Clean(Select(after, fields))),
                ["deltaDifferences"] = Differences(
                    Comparison.DeltaView(Comparison.Diff(client.Redactor.Clean(Select(before, fields)), client.Redactor.Clean(Select(expectedAfter, fields)))),
                    Comparison.DeltaView(Comparison.Diff(client.Redactor.Clean(Select(before, fields)), client.Redactor.Clean(Select(after, fields))))),
                ["expectedResponse"] = client.Redactor.Clean(expected),
                ["actualResponse"] = client.Redactor.Clean(response)
            };
            results.Add(report);
            JsonFiles.Write(Path.Combine(output, "case-compare.json"), results);
            if (!report["httpStatusMatches"]!.GetValue<bool>() || report.Where(pair => pair.Key.EndsWith("Differences", StringComparison.Ordinal))
                .Any(pair => pair.Value!.AsArray().Count != 0))
                throw new InvalidOperationException("完整响应或独立回读有差异，见报告。");
            Console.WriteLine($"{action} {fixture["label"]!.GetValue<string>()} 对拍通过。");
        }
        Console.WriteLine($"{operation} 共 {results.Count} 组完整响应及独立回读对拍通过。");
    }

    private static void Remap(JsonNode? node)
    {
        if (node is JsonArray array) foreach (var child in array) Remap(child);
        if (node is not JsonObject obj) return;
        foreach (var key in obj.Select(pair => pair.Key).ToArray())
            if (key == "userId") obj[key] = 1;
            else Remap(obj[key]);
    }

    private static Dictionary<string, string> Map(JsonNode? node) => node?.AsObject()
        .ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>()) ?? [];

    private static JsonObject Select(JsonNode? source, string[] fields) => new(fields.Where(field => source?.AsObject().ContainsKey(field) == true)
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
