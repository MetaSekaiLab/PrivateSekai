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

internal static class ChallengeHighScoreReplay
{
    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "challengeLiveHighScoreRewards", "resourceBoxes" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static void Run(IServiceProvider provider, MemoryUserStore store, string path, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        Directory.CreateDirectory(output);
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (official["operation"]?.GetValue<string>() != "challenge-live-clear" || official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的官方挑战结算记录。");
        var session = official["args"]!["userChallengeLiveId"]!.GetValue<string>();
        var character = official["before"]!["userChallengeLivePlayStatuses"]!.AsArray()
            .Single(s => s!["userChallengeLiveId"]!.GetValue<string>() == session)!["characterId"]!.GetValue<int>();
        var state = store.Read(1)!;
        state.Data.userChallengeLiveSoloResults = JsonSerializer.Deserialize<UserChallengeLiveSoloResult[]>(
            official["before"]!["userChallengeLiveSoloResults"]!.ToJsonString(), DumpJson.Options)!;
        state.Data.userChallengeLiveSoloHighScoreRewards = JsonSerializer.Deserialize<UserChallengeLiveHighScoreReward[]>(
            official["before"]!["userChallengeLiveSoloHighScoreRewards"]!.ToJsonString(), DumpJson.Options)!;
        store.Save(1, state);
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var service = scope.ServiceProvider.GetRequiredService<ChallengeLiveService>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        UserChallengeLiveHighScoreResult result = null!;
        operations.Execute(1, () =>
        {
            result = service.UpdateHighScore(character, official["request"]!["score"]!.GetValue<int>());
            return user.BuildRefresh();
        });
        var actual = JsonSerializer.SerializeToNode(result, DumpJson.Options);
        var expected = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<UserChallengeLiveHighScoreResult>(
            official["response"]!["userChallengeLiveHighScoreResult"]!.ToJsonString(), DumpJson.Options), DumpJson.Options);
        var after = store.Read(1)!.Data;
        var resultDifferences = Comparison.Diff(expected, actual);
        var stateDifferences = Comparison.Diff(new JsonObject
        {
            ["userChallengeLiveSoloResults"] = official["after"]!["userChallengeLiveSoloResults"]!.DeepClone(),
            ["userChallengeLiveSoloHighScoreRewards"] = official["after"]!["userChallengeLiveSoloHighScoreRewards"]!.DeepClone()
        }, new JsonObject
        {
            ["userChallengeLiveSoloResults"] = JsonSerializer.SerializeToNode(after.userChallengeLiveSoloResults, DumpJson.Options),
            ["userChallengeLiveSoloHighScoreRewards"] = JsonSerializer.SerializeToNode(after.userChallengeLiveSoloHighScoreRewards, DumpJson.Options)
        });
        JsonFiles.Write(Path.Combine(output, "challenge-high-score-compare.json"), new
        {
            scope = "高分业务重放；比较解码后结果及成绩、领奖记录，不覆盖完整 HTTP 结算",
            expected, actual, resultDifferences, stateDifferences
        });
        if (resultDifferences.Count != 0 || stateDifferences.Count != 0)
            throw new InvalidOperationException("挑战高分与官方样本存在差异，见重放报告。");
        Console.WriteLine("挑战高分业务重放通过；不代表完整结算接口通过。");
    }
}
