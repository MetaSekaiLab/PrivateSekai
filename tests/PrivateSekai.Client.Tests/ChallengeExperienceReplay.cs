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

internal static class ChallengeExperienceReplay
{
    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "cards", "cardRarities", "levels", "musicDifficulties", "playLevelScores",
                     "playerRankRewards", "releaseConditions", "resourceBoxes", "configs" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static void Run(IServiceProvider provider, MemoryUserStore store, string path, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        Directory.CreateDirectory(output);
        var record = JsonNode.Parse(File.ReadAllText(path))!;
        if (record["operation"]?.GetValue<string>() != "challenge-live-clear" || record["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功的官方挑战结算记录。");
        var before = record["before"]!.DeepClone();
        string[] fields = ["userGamedata", "userCards", "userBoost", "userChargedCurrency", "userMaterials",
            "userReleaseConditions", "userColorfulPassV2"];
        foreach (var field in fields)
        {
            if (before[field] is JsonObject obj && obj["userId"] != null) obj["userId"] = 1;
            if (before[field] is JsonArray rows)
                foreach (var row in rows)
                    if (row?["userId"] != null) row["userId"] = 1;
        }
        var state = store.Read(1)!;
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => fields.Contains((string)m.Key)))
            if (before[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        var session = record["before"]!["userChallengeLivePlayStatuses"]!.AsArray().Single(s =>
            s!["userChallengeLiveId"]!.GetValue<string>() == record["args"]!["userChallengeLiveId"]!.GetValue<string>())!;
        var deck = record["before"]!["userChallengeLiveSoloDecks"]!.AsArray().Single(d =>
            d!["characterId"]!.GetValue<int>() == session["characterId"]!.GetValue<int>())!;
        var startNode = deck.DeepClone();
        startNode["musicDifficultyId"] = session["musicDifficultyId"]!.DeepClone();
        startNode["isAuto"] = session["isAuto"]!.DeepClone();
        var start = JsonSerializer.Deserialize<UserChallengeLiveStartRequest>(startNode.ToJsonString(), DumpJson.Options)!;
        var clear = JsonSerializer.Deserialize<UserChallengeLiveClearRequest>(record["request"]!.ToJsonString(), DumpJson.Options)!;
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ChallengeLiveService>();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        JsonObject actual = new();
        operation.Execute(1, () =>
        {
            var result = service.GainExperience(start, clear);
            actual = new JsonObject
            {
                ["userExpResult"] = JsonSerializer.SerializeToNode(result.Player, DumpJson.Options),
                ["deckCardExpResults"] = JsonSerializer.SerializeToNode(result.Cards, DumpJson.Options),
                ["playerRankRewards"] = JsonSerializer.SerializeToNode(result.Rewards, DumpJson.Options)
            };
            return user.BuildRefresh();
        });
        // 只反序列化所比较的字段，响应其他部分可能包含脱敏用户字段。
        var expected = new JsonObject
        {
            ["userExpResult"] = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<UpdateExpResult>(record["response"]!["userExpResult"]!.ToJsonString(), DumpJson.Options), DumpJson.Options),
            ["deckCardExpResults"] = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<DeckCardUpdateExpResult[]>(record["response"]!["deckCardExpResults"]!.ToJsonString(), DumpJson.Options), DumpJson.Options),
            ["playerRankRewards"] = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<UserResource[]>(record["response"]!["playerRankRewards"]!.ToJsonString(), DumpJson.Options), DumpJson.Options)
        };
        var differences = Comparison.Diff(expected, actual);
        var saved = store.Read(1)!.Data;
        var expectedCards = record["after"]!["userCards"]!.DeepClone();
        foreach (var card in expectedCards.AsArray())
            if (card?["userId"] != null) card["userId"] = 1;
        var cardDifferences = Comparison.Diff(JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<UserCard[]>(
            expectedCards.ToJsonString(), DumpJson.Options), DumpJson.Options),
            JsonSerializer.SerializeToNode(saved.userCards, DumpJson.Options));
        var player = record["after"]!["userGamedata"]!;
        if (saved.userGamedata.totalExp != player["totalExp"]!.GetValue<int>() ||
            saved.userGamedata.rank != player["rank"]!.GetValue<int>() || saved.userGamedata.exp != player["exp"]!.GetValue<int>())
            throw new InvalidOperationException("挑战玩家经验持久状态不一致。");
        JsonFiles.Write(Path.Combine(output, "challenge-experience-compare.json"), new { expected, actual, differences, cardDifferences });
        if (differences.Count != 0 || cardDifferences.Count != 0)
            throw new InvalidOperationException("挑战经验与官方样本存在差异。");
        var persisted = JsonSerializer.Serialize(saved, DumpJson.Options);
        start.support1 = int.MaxValue;
        var rejected = false;
        try { operation.Execute(1, () => service.GainExperience(start, clear)); }
        catch (ArgumentException) { rejected = true; }
        if (!rejected || persisted != JsonSerializer.Serialize(store.Read(1)!.Data, DumpJson.Options))
            throw new InvalidOperationException("后续卡牌发放失败未回滚玩家和领队经验。");
        Console.WriteLine("挑战玩家及卡牌经验业务回放通过；不代表完整 HTTP 结算通过。");
    }
}
