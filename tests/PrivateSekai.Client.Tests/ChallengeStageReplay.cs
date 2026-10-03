extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Client;
using PrivateSekai.Modules.Live;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

internal static class ChallengeStageReplay
{
    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "challengeLiveStages", "resourceBoxes", "levels", "characterRanks", "musicDifficulties", "playLevelScores", "configs", "liveMissions", "beginnerMissionV2s" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static void Run(IServiceProvider provider, MemoryUserStore store, string path, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        Directory.CreateDirectory(output);
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (official["operation"]?.GetValue<string>() != "challenge-live-clear" ||
            official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的官方挑战结算记录。");
        var expected = official["response"]!["userChallengeLiveStageResult"]!;
        var sessionId = official["args"]!["userChallengeLiveId"]!.GetValue<string>();
        var playStatus = official["before"]!["userChallengeLivePlayStatuses"]!.AsArray()
            .Single(s => s!["userChallengeLiveId"]!.GetValue<string>() == sessionId)!;
        var characterId = playStatus["characterId"]!.GetValue<int>();
        var state = store.Read(1)!;
        state.Data.userChallengeLiveSoloStages = JsonSerializer.Deserialize<UserChallengeLiveSoloStage[]>(
            official["before"]!["userChallengeLiveSoloStages"]!.ToJsonString(), DumpJson.Options)!;
        state.Data.userCharacters = JsonSerializer.Deserialize<UserCharacter[]>(
            official["before"]!["userCharacters"]!.ToJsonString(), DumpJson.Options)!;
        state.Data.userChargedCurrency = new() { paidUnitPrices = [] };
        state.Data.userColorfulPassV2 = official["before"]?["userColorfulPassV2"]?.Deserialize<UserColorfulPassV2>(DumpJson.Options);
        state.Data.userLiveMissions = official["before"]!["userLiveMissions"]!.Deserialize<UserLiveMission[]>(DumpJson.Options);
        state.Data.userMissionStatuses = official["before"]!["userMissionStatuses"]!.Deserialize<UserMissionStatus[]>(DumpJson.Options);
        state.Data.userBeginnerMissionV2s = official["before"]!["userBeginnerMissionV2s"]!.Deserialize<UserBeginnerMissionV2[]>(DumpJson.Options);
        foreach (var mission in state.Data.userLiveMissions ?? []) mission.userId = 1;
        foreach (var status in state.Data.userMissionStatuses ?? []) status.userId = 1;
        var periodId = (state.Data.userLiveMissions ?? []).Single(m => m.liveMissionStatus == "free").liveMissionPeriodId;
        store.Save(1, state);
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var service = scope.ServiceProvider.GetRequiredService<ChallengeLiveService>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var master = scope.ServiceProvider.GetRequiredService<LiveMasterQueries>();
        var scoreRank = master.GetChallengeScoreRank(playStatus["musicDifficultyId"]!.GetValue<int>(),
            official["request"]!["score"]!.GetValue<int>());
        if (scoreRank != official["response"]!["scoreRank"]!.GetValue<string>())
            throw new InvalidOperationException("挑战评分与官方样本存在差异。");
        JsonObject actual = new();
        var start = new UserChallengeLiveStartRequest
        {
            characterId = characterId, isAuto = playStatus["isAuto"]?.GetValue<bool>() ?? false
        };
        var clear = official["request"]!.Deserialize<UserChallengeLiveClearRequest>(DumpJson.Options)!;
        operations.Execute(1, () =>
        {
            var result = service.AdvanceStage(start, clear);
            service.UpdateMissions(start, clear, periodId);
            actual = JsonSerializer.SerializeToNode(result, DumpJson.Options)!.AsObject();
            return user.BuildRefresh();
        });
        // 比较解码后的业务资源；网络字段省略规则需在完整结算接口中另行核验。
        var projected = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<UserChallengeLiveStageResult>(
            expected.ToJsonString(), DumpJson.Options), DumpJson.Options);
        var after = JsonSerializer.SerializeToNode(store.Read(1)!.Data.userChallengeLiveSoloStages, DumpJson.Options);
        var differences = Comparison.Diff(projected, actual);
        var stageDifferences = Comparison.Diff(official["after"]!["userChallengeLiveSoloStages"], after);
        var characterDifferences = Comparison.Diff(JsonSerializer.SerializeToNode(
                JsonSerializer.Deserialize<UserCharacter[]>(official["after"]!["userCharacters"]!.ToJsonString(), DumpJson.Options), DumpJson.Options),
            JsonSerializer.SerializeToNode(store.Read(1)!.Data.userCharacters, DumpJson.Options));
        var saved = store.Read(1)!.Data;
        var expectedMissions = new
        {
            live = official["after"]!["userLiveMissions"]!.Deserialize<UserLiveMission[]>(DumpJson.Options),
            beginner = official["after"]!["userBeginnerMissionV2s"]!.Deserialize<UserBeginnerMissionV2[]>(DumpJson.Options),
            statuses = official["after"]!["userMissionStatuses"]!.Deserialize<UserMissionStatus[]>(DumpJson.Options)
        };
        var actualMissions = new { live = saved.userLiveMissions, beginner = saved.userBeginnerMissionV2s, statuses = saved.userMissionStatuses };
        // 用户标识映射到本地测试账号；官方资源通常省略该字段。
        foreach (var mission in expectedMissions.live ?? []) mission.userId = 1;
        foreach (var status in expectedMissions.statuses ?? []) status.userId = 1;
        var missionDifferences = Comparison.Diff(JsonSerializer.SerializeToNode(expectedMissions, DumpJson.Options),
            JsonSerializer.SerializeToNode(actualMissions, DumpJson.Options));
        JsonFiles.Write(Path.Combine(output, "challenge-stage-compare.json"), new
        {
            scope = "评分、普通挑战点数、阶段、角色升级和任务业务重放；任务周期取结算前状态，不验证跨期或完整 HTTP 结算",
            scoreRank,
            expected = projected, actual,
            resultDifferences = differences, stageDifferences, characterDifferences, missionDifferences,
            actualStages = after
        });
        if (differences.Count != 0 || stageDifferences.Count != 0 || characterDifferences.Count != 0 || missionDifferences.Count != 0)
            throw new InvalidOperationException("挑战阶段与官方样本存在差异，见重放报告。");
        Console.WriteLine("挑战阶段业务重放通过；不代表完整结算接口通过。");
    }
}
