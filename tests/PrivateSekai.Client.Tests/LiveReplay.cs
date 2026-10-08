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
    private static HashSet<int> rankReleaseIds = [];
    private static readonly string[] Fields = ["userGamedata", "userCards", "userDecks", "userBoost",
        "userMaterials", "userChargedCurrency", "userMusicResults", "userMusicAchievements", "userLiveMissions",
        "userMissionStatuses", "userHonorMissions", "userBeginnerMissionV2s", "userLiveCharacterArchiveVoice", "userEventBreakTime", "userAutoLive", "userReleaseConditions",
        "userCharacterLiveUsageCounts", "userCharacterMissionV2s", "userCharacterMissionV2Statuses"];

    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "cards", "cardRarities", "musicDifficulties", "playLevelScores",
            "boosts", "musicAchievements", "resourceBoxes", "liveMissionPeriods", "liveMissions", "beginnerMissionV2s", "levels", "playerRankRewards", "configs", "releaseConditions",
            "characterMissionV2s", "characterMissionV2ParameterGroups", "honorMissions" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
        rankReleaseIds = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "releaseConditions.json")))!.AsArray()
            .Where(c => c?["releaseConditionType"]?.GetValue<string>() == "user_rank")
            .Select(c => c!["id"]!.GetValue<int>()).ToHashSet();
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

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string startPath, string clearPath, string output,
        ProtocolClient? honorReadback = null)
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
            {
                var local = JsonNode.Parse(File.ReadAllText(localPath))!.AsObject();
                if (honorReadback != null)
                {
                    var report = ScenarioRunner.Compare(SelectHonorRecord(official), SelectHonorRecord(local));
                    await honorReadback.Send(new() { Operation = "system" });
                    var after = await honorReadback.Suite();
                    var differences = Comparison.Diff(SelectHonor(official["after"]), SelectHonor(after));
                    report["readbackDifferences"] = JsonSerializer.SerializeToNode(differences, JsonFiles.Options);
                    JsonFiles.Write(Path.Combine(output, "honor-progress-compare.json"), report);
                    if (differences.Count != 0 || !report["complete"]!.GetValue<bool>() ||
                        new[] { "httpStatusDifferences", "baselineDifferences", "responseDifferences", "deltaDifferences" }
                            .Any(k => report[k]!.AsArray().Count != 0))
                        throw new InvalidOperationException("Easy FC 进度、状态或独立回读与官方不同。");
                    Console.WriteLine("Easy FC 进度、任务状态、临时达成提示及独立回读 HTTP 对拍通过。");
                }
                JsonFiles.Write(Path.Combine(output, "full-compare.json"), ScenarioRunner.Compare(official,
                    local));
                JsonFiles.Write(Path.Combine(output, "rank-release-compare.json"),
                    ScenarioRunner.Compare(SelectRankReleaseRecord(official), SelectRankReleaseRecord(local)));
                JsonFiles.Write(Path.Combine(output, "boost-compare.json"),
                    ScenarioRunner.Compare(SelectBoostRecord(official), SelectBoostRecord(local)));
                JsonFiles.Write(Path.Combine(output, "live-mission-compare.json"),
                    ScenarioRunner.Compare(SelectMissionRecord(official), SelectMissionRecord(local)));
                JsonFiles.Write(Path.Combine(output, "leader-usage-compare.json"),
                    ScenarioRunner.Compare(SelectLeaderRecord(official), SelectLeaderRecord(local)));
                JsonFiles.Write(Path.Combine(output, "character-usage-compare.json"),
                    ScenarioRunner.Compare(SelectLeaderRecord(official, false), SelectLeaderRecord(local, false)));
                foreach (var record in new[] { official, local })
                {
                    foreach (var side in new[] { "before", "after" })
                        record[side] = SelectExperience(record[side]);
                    var response = record["response"];
                    record["response"] = new JsonObject
                    {
                        ["userExpResult"] = response?["userExpResult"]?.DeepClone(),
                        ["deckCardExpResults"] = response?["deckCardExpResults"]?.DeepClone(),
                        ["updatedResources"] = SelectExperience(response?["updatedResources"])
                    };
                }
                JsonFiles.Write(Path.Combine(output, "experience-compare.json"), ScenarioRunner.Compare(official, local));
            }
        }
    }

    private static JsonObject SelectBoostRecord(JsonObject record)
    {
        var selected = record.DeepClone().AsObject();
        foreach (var side in new[] { "before", "after" })
            selected[side] = Select(record[side], ["userBoost"]);
        selected["response"] = new JsonObject
        {
            ["updatedResources"] = Select(record["response"]?["updatedResources"], ["userBoost"])
        };
        return selected;
    }

    private static JsonObject SelectHonor(JsonNode? source)
    {
        var selected = new JsonObject();
        if (source?["userHonorMissions"] is JsonArray missions)
            selected["userHonorMissions"] = new JsonArray(missions.Where(m => m?["honorMissionType"]?.GetValue<string>() == "easy_full_combo")
                .Select(m => m!.DeepClone()).ToArray());
        if (source?["userMissionStatuses"] is JsonArray statuses)
            selected["userMissionStatuses"] = new JsonArray(statuses.Where(m => m?["missionType"]?.GetValue<string>() == "honor_mission")
                .Select(m => m!.DeepClone()).ToArray());
        return selected;
    }

    private static JsonObject SelectHonorRecord(JsonObject record)
    {
        var selected = record.DeepClone().AsObject();
        foreach (var side in new[] { "before", "after" }) selected[side] = SelectHonor(record[side]);
        selected["response"] = new JsonObject
        {
            ["fullComboFlg"] = record["response"]?["fullComboFlg"]?.DeepClone(),
            ["fullPerfectFlg"] = record["response"]?["fullPerfectFlg"]?.DeepClone(),
            ["updatedResources"] = SelectHonor(record["response"]?["updatedResources"])
        };
        return selected;
    }

    private static JsonObject SelectLeaderRecord(JsonObject record, bool leaderOnly = true)
    {
        JsonObject? Project(JsonNode? source)
        {
            var selected = Select(source, ["userCharacterLiveUsageCounts", "userCharacterMissionV2s", "userCharacterMissionV2Statuses"]);
            if (leaderOnly && selected?["userCharacterLiveUsageCounts"] is JsonArray counts)
                selected["userCharacterLiveUsageCounts"] = new JsonArray(counts
                    .Where(c => c?["characterLiveUsageType"]?.GetValue<string>() == "leader")
                    .Select(c => c!.DeepClone()).ToArray());
            return selected;
        }
        var result = record.DeepClone().AsObject();
        foreach (var side in new[] { "before", "after" }) result[side] = Project(record[side]);
        result["response"] = new JsonObject { ["updatedResources"] = Project(record["response"]?["updatedResources"]) };
        return result;
    }

    private static JsonObject SelectMissionRecord(JsonObject record)
    {
        var selected = record.DeepClone().AsObject();
        string[] fields = ["userLiveMissions", "userMissionStatuses", "userBeginnerMissionV2s"];
        foreach (var side in new[] { "before", "after" })
            selected[side] = Select(record[side], fields);
        selected["response"] = new JsonObject
        {
            ["updatedResources"] = Select(record["response"]?["updatedResources"], fields)
        };
        return selected;
    }

    private static JsonObject SelectRankReleaseRecord(JsonObject record)
    {
        var selected = record.DeepClone().AsObject();
        foreach (var side in new[] { "before", "after" })
            selected[side] = SelectRankReleases(record[side]);
        selected["response"] = new JsonObject
        {
            ["updatedResources"] = SelectRankReleases(record["response"]?["updatedResources"])
        };
        return selected;
    }

    private static JsonObject? SelectRankReleases(JsonNode? source) => source == null ? null : new()
    {
        ["userReleaseConditions"] = source["userReleaseConditions"] is JsonArray conditions
            ? new JsonArray(conditions.Where(c => rankReleaseIds.Contains(c!["releaseConditionId"]!.GetValue<int>()))
                .Select(c => c!.DeepClone()).ToArray()) : null
    };

    private static JsonObject SelectExperience(JsonNode? source) => new()
    {
        ["userGamedata"] = Select(source?["userGamedata"], ["rank", "exp", "totalExp"]),
        ["userCards"] = source?["userCards"] is JsonArray cards
            ? new JsonArray(cards.Select(card => (JsonNode?)Select(card, ["cardId", "level", "exp", "totalExp"])).ToArray())
            : null
    };

    private static JsonObject? Select(JsonNode? source, string[] fields) => source == null ? null :
        new(fields.Where(field => source.AsObject().ContainsKey(field))
            .Select(field => KeyValuePair.Create(field, source![field]?.DeepClone())));

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
