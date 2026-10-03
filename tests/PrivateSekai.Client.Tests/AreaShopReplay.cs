extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class AreaShopReplay
{
    private static readonly string[] Fields = ["userAreas", "userShops", "userMaterials", "userGamedata", "userCharacterMissionV2s", "userCharacterMissionV2Statuses", "userBeginnerMissionV2s", "userMissionStatuses"];

    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "shopItems", "resourceBoxes", "areaItems", "musicVocals", "characterMissionV2AreaItems", "characterMissionV2s", "characterMissionV2ParameterGroups", "beginnerMissionV2s", "gameCharacters" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    private static JsonObject Read(string path)
    {
        var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (record["operation"]?.GetValue<string>() is not ("shop-purchase" or "shop-upgrade") ||
            record["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的官方商店请求记录。");
        return record;
    }

    public static TimeProvider Clock(string path) => new ReplayClock(Read(path)["response"]!["updatedResources"]!["now"]!.GetValue<long>());
    private sealed class ReplayClock(long now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(now);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string path, string output, bool music = false)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        string[] fields = music ? ["userMusics", "userMusicVocals", "userShops", "userMaterials", "userBeginnerMissionV2s", "userMissionStatuses"] : Fields;
        var official = Read(path);
        var state = store.Read(1)!;
        var before = official["before"]!.DeepClone();
        before["userGamedata"]!["userId"] = 1;
        foreach (var status in before["userCharacterMissionV2Statuses"]?.AsArray() ?? [])
            status!["userId"] = 1;
        foreach (var status in before["userMissionStatuses"]?.AsArray() ?? [])
            if (status?["userId"] != null) status["userId"] = 1;
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => fields.Contains((string)m.Key)))
            if (before[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = official["operation"]!.GetValue<string>(),
            Args = JsonSerializer.Deserialize<Dictionary<string, string>>(official["args"]!.ToJsonString())!
        }] }, output);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official, local));
        foreach (var record in new[] { official, local })
        {
            foreach (var side in new[] { "before", "after" }) record[side] = Select(record[side], fields);
            record["response"]!["updatedResources"] = Select(record["response"]!["updatedResources"], fields);
        }
        var report = ScenarioRunner.Compare(official, local);
        JsonFiles.Write(Path.Combine(output, music ? "music-shop-compare.json" : "area-shop-compare.json"), report);
        if (music && (!report["complete"]!.GetValue<bool>() ||
            new[] { "httpStatusDifferences", "baselineDifferences", "responseDifferences", "deltaDifferences" }
                .Any(k => report[k]!.AsArray().Count != 0)))
            throw new InvalidOperationException("音乐商店与官方样本存在差异，见报告。");
    }

    private static JsonObject Select(JsonNode? source, string[] fields) => new(fields.Select(field => KeyValuePair.Create(field, source?[field]?.DeepClone())));
}
