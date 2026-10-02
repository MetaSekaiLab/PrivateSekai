extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Models;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class LiveReplay
{
    private static readonly string[] Fields = ["userGamedata", "userCards", "userDecks", "userBoost",
        "userMaterials", "userChargedCurrency", "userMusicResults", "userMusicAchievements", "userLiveMissions",
        "userMissionStatuses", "userLiveCharacterArchiveVoice", "userEventBreakTime"];

    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "cards", "musicDifficulties", "playLevelScores",
            "boosts", "musicAchievements", "resourceBoxes", "liveMissionPasses", "levels", "playerRankRewards", "configs" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    private static JsonObject Read(string path, string operation)
    {
        var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (record["operation"]?.GetValue<string>() != operation || record["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的 Live 开局和结算记录。");
        return record;
    }

    public static TimeProvider Clock(string path) => new ReplayClock(Read(path, "live-clear")["response"]!["updatedResources"]!["now"]!.GetValue<long>());
    private sealed class ReplayClock(long now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(now);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string startPath, string clearPath, string output)
    {
        var start = Read(startPath, "live-start");
        var official = Read(clearPath, "live-clear");
        var liveId = start["response"]!["userLiveId"]!.GetValue<string>();
        if (official["args"]!["userLiveId"]!.GetValue<string>() != liveId)
            throw new InvalidOperationException("开局和结算记录不是同一演出。");
        var request = JsonSerializer.Deserialize<UserLiveRequest>(start["request"]!.ToJsonString(), DumpJson.Options)!;
        var state = store.Read(1)!;
        var before = official["before"]!.DeepClone();
        BindUser(before);
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => Fields.Contains((string)m.Key)))
            if (before[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        state.Private.UserLiveSessions[liveId] = new UserLiveSessionData
        {
            UserLiveId = liveId, MusicId = request.musicId, MusicDifficultyId = request.musicDifficultyId,
            MusicVocalId = request.musicVocalId, DeckId = request.deckId, BoostCount = request.boostCount,
            IsAuto = request.isAuto, MusicCategoryName = request.musicCategoryName,
            CustomMusicScoreId = request.customMusicScoreId, CreatedAt = start["before"]!["now"]!.GetValue<long>()
        };
        store.Save(1, state);
        await client.Send(new() { Operation = "system" });
        try
        {
            await ScenarioRunner.Run(client, new() { Steps = [new()
            {
                Operation = "live-clear", Args = new() { ["userLiveId"] = liveId },
                Body = official["request"]!.DeepClone().AsObject()
            }] }, output);
        }
        finally
        {
            var localPath = Path.Combine(output, "001.json");
            if (File.Exists(localPath))
                JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official,
                    JsonNode.Parse(File.ReadAllText(localPath))!.AsObject()));
        }
    }

    private static void BindUser(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var field in obj.ToArray())
                if (field.Key == "userId") obj[field.Key] = 1;
                else BindUser(field.Value);
        else if (node is JsonArray array)
            foreach (var item in array) BindUser(item);
    }
}
