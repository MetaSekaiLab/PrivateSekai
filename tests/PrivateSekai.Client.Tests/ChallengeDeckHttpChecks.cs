using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Storage;

internal static class ChallengeDeckHttpChecks
{
    public static void WriteMaster(string directory)
    {
        // 隔离测试条件，不代表官方配置。
        File.WriteAllText(Path.Combine(directory, "challengeLiveCharacters.json"),
            """[{"id":1,"characterId":1,"releaseConditionId":90001,"orReleaseConditionId":90002}]""");
        File.WriteAllText(Path.Combine(directory, "challengeLiveDecks.json"), "[]");
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userCards = [new() { cardId = 1 }];
        state.Data.userReleaseConditions = [new() { releaseConditionId = 90002 }];
        state.Data.userChallengeLiveSoloDecks = [new() { characterId = 2, leader = 9 }];
        state.Data.userChallengeLiveSoloStages = [new() { characterId = 2, rank = 3, point = 20 }];
        store.Save(1, state);
        await ScenarioRunner.Run(client, new() { Steps = [Step(1), Step(1)] }, Path.Combine(directory, "challenge-deck"));
        var saved = store.Read(1)!.Data;
        check(saved.userChallengeLiveSoloDecks.Length == 2 &&
            saved.userChallengeLiveSoloDecks.Single(d => d.characterId == 1).leader == 1 &&
            saved.userChallengeLiveSoloDecks.Single(d => d.characterId == 2).leader == 9,
            "挑战编队重复保存不新增重复条目，也不覆盖其他角色");
        check(saved.userChallengeLiveSoloStages.Single().point == 20,
            "保存挑战编队不创建或重置挑战阶段");
        var response = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "challenge-deck/001.json")))!["response"]!;
        check(JsonNode.DeepEquals(response["userChallengeLiveSoloDeck"], JsonNode.Parse("""{"characterId":1,"leader":1}""")) &&
            response["updatedResources"]!["userChallengeLiveSoloDecks"]!.AsArray().Count == 2 &&
            response["updatedResources"]!["userChallengeLivePlayStatuses"]!.AsArray().Count == 0,
            "挑战编队响应包含私有协议字段与刷新组，省略空支援位");
        foreach (var step in new[] { Step(999), Step(1, 1), Step(1, null, 2) })
        {
            using var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await rejected.Send(new() { Operation = "system" });
            var failed = false;
            try { await rejected.Send(step); }
            catch (ClientFailure) { failed = true; }
            check(failed && store.Read(1)!.Data.userChallengeLiveSoloDecks.Single(d => d.characterId == 1).leader == 1,
                "拒绝未持有卡、重复卡或路径角色不匹配，保留原编队");
        }
        state = store.Read(1)!;
        state.Data.userReleaseConditions = [];
        store.Save(1, state);
        using var locked = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await locked.Send(new() { Operation = "system" });
        var lockedFailed = false;
        try { await locked.Send(Step(1)); }
        catch (ClientFailure) { lockedFailed = true; }
        check(lockedFailed && store.Read(1)!.Data.userChallengeLiveSoloDecks.Length == 2,
            "未解锁角色不能保存挑战编队");
        await client.Send(new() { Operation = "system" });
    }

    private static ScenarioStep Step(int leader, int? support = null, int character = 1) => new()
    {
        Operation = "challenge-deck-save", Args = new() { ["characterId"] = "1" },
        Body = new() { ["characterId"] = character, ["leader"] = leader, ["support1"] = support }
    };
}
