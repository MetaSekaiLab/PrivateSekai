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
        foreach (var table in new[] { "challengeLiveStages", "resourceBoxes", "levels", "characterRanks" })
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
        var characterId = official["before"]!["userChallengeLivePlayStatuses"]!.AsArray()
            .Single(s => s!["userChallengeLiveId"]!.GetValue<string>() == sessionId)!["characterId"]!.GetValue<int>();
        var state = store.Read(1)!;
        state.Data.userChallengeLiveSoloStages = JsonSerializer.Deserialize<UserChallengeLiveSoloStage[]>(
            official["before"]!["userChallengeLiveSoloStages"]!.ToJsonString(), DumpJson.Options)!;
        state.Data.userCharacters = JsonSerializer.Deserialize<UserCharacter[]>(
            official["before"]!["userCharacters"]!.ToJsonString(), DumpJson.Options)!;
        state.Data.userChargedCurrency = new() { paidUnitPrices = [] };
        store.Save(1, state);
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var service = scope.ServiceProvider.GetRequiredService<ChallengeLiveService>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        JsonObject actual = new();
        operations.Execute(1, () =>
        {
            var result = service.AdvanceStage(characterId, expected["addPoint"]!.GetValue<int>());
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
        JsonFiles.Write(Path.Combine(output, "challenge-stage-compare.json"), new
        {
            scope = "阶段及角色升级业务重放；点数取官方响应，不验证点数公式或完整 HTTP 结算",
            expected = projected, actual,
            resultDifferences = differences, stageDifferences, characterDifferences,
            actualStages = after
        });
        if (differences.Count != 0 || stageDifferences.Count != 0 || characterDifferences.Count != 0)
            throw new InvalidOperationException("挑战阶段与官方样本存在差异，见重放报告。");
        Console.WriteLine("挑战阶段业务重放通过；不代表完整结算接口通过。");
    }
}
