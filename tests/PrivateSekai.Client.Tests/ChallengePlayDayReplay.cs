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

internal static class ChallengePlayDayReplay
{
    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "configs", "challengeLivePlayDayRewardPeriods", "resourceBoxes" })
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
        var sessionId = official["args"]!["userChallengeLiveId"]!.GetValue<string>();
        var start = official["before"]!["userChallengeLivePlayStatuses"]!.AsArray()
            .Single(s => s!["userChallengeLiveId"]!.GetValue<string>() == sessionId)!["playStartAt"]!.GetValue<long>();
        var state = store.Read(1)!;
        state.Data.userChallengeLivePlayDay = official["before"]!["userChallengeLivePlayDay"] is { } before
            ? JsonSerializer.Deserialize<UserChallengeLivePlayDay>(before.ToJsonString(), DumpJson.Options) : null;
        store.Save(1, state);
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var service = scope.ServiceProvider.GetRequiredService<ChallengeLiveService>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        UserResource[] rewards = [];
        operations.Execute(1, () =>
        {
            rewards = service.RecordFirstPlayDay(start);
            return user.BuildRefresh();
        });
        var day = store.Read(1)!.Data.userChallengeLivePlayDay;
        var actual = new JsonObject
        {
            ["challengeLivePlayDays"] = day.playDays,
            ["challengeLivePlayDayRewardStatus"] = day.challengeLivePlayDayRewardStatus,
            ["challengeLivePlayDayRewards"] = JsonSerializer.SerializeToNode(rewards, DumpJson.Options)
        };
        var expected = new JsonObject
        {
            ["challengeLivePlayDays"] = official["response"]!["challengeLivePlayDays"]!.DeepClone(),
            ["challengeLivePlayDayRewardStatus"] = official["response"]!["challengeLivePlayDayRewardStatus"]!.DeepClone(),
            ["challengeLivePlayDayRewards"] = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<UserResource[]>(
                official["response"]!["challengeLivePlayDayRewards"]!.ToJsonString(), DumpJson.Options), DumpJson.Options)
        };
        var resultDifferences = Comparison.Diff(expected, actual);
        var stateDifferences = Comparison.Diff(official["after"]!["userChallengeLivePlayDay"], JsonSerializer.SerializeToNode(day, DumpJson.Options));
        JsonFiles.Write(Path.Combine(output, "challenge-play-day-compare.json"), new
        {
            scope = "首次出勤业务重放；不覆盖后续出勤、跨日、选择奖励或完整 HTTP 结算",
            expected, actual, resultDifferences, stateDifferences
        });
        if (resultDifferences.Count != 0 || stateDifferences.Count != 0)
            throw new InvalidOperationException("首次出勤与官方样本存在差异，见重放报告。");
        Console.WriteLine("首次出勤业务重放通过；不代表完整结算接口通过。");
    }
}
