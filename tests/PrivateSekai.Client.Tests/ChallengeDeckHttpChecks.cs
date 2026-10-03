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
        File.WriteAllText(Path.Combine(directory, "challengeLives.json"), """[{"id":1,"playableCount":1}]""");
        var configsPath = Path.Combine(directory, "configs.json");
        var configs = JsonNode.Parse(File.ReadAllText(configsPath))!.AsArray();
        configs.Add(JsonNode.Parse("""{"configKey":"default_challenge_live_deck_limit","value":"1"}"""));
        JsonFiles.Write(configsPath, configs);
        File.WriteAllText(Path.Combine(directory, "oneTimeBehaviors.json"),
            """[{"id":1,"oneTimeBehaviorType":"challenge_live_character_force_release","releaseConditionId":880005}]""");
        var conditionsPath = Path.Combine(directory, "releaseConditions.json");
        var conditions = JsonNode.Parse(File.ReadAllText(conditionsPath))!.AsArray();
        conditions.Add(JsonNode.Parse("""{"id":880005,"releaseConditionType":"user_rank","releaseConditionTypeLevel":5}"""));
        File.WriteAllText(conditionsPath, conditions.ToJsonString());
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
        state = store.Read(1)!;
        state.Data.userOneTimeBehaviors = [];
        state.Data.userGamedata.rank = 4;
        store.Save(1, state);
        using var unlock = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await unlock.Send(new() { Operation = "system" });
        var unlockStep = new ScenarioStep { Operation = "challenge-character-unlock", Args = new() { ["characterId"] = "1" } };
        var lowRankFailed = false;
        try { await unlock.Send(unlockStep); }
        catch (ClientFailure) { lowRankFailed = true; }
        check(lowRankFailed && unlock.LastHttpStatus == 409 && store.Read(1)!.Data.userOneTimeBehaviors.Length == 0,
            "首次挑战解锁要求 master 玩家等级，失败不产生一次行为");
        state = store.Read(1)!;
        state.Data.userGamedata.rank = 5;
        store.Save(1, state);
        using var eligible = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await eligible.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(eligible, new() { Steps = [unlockStep] }, Path.Combine(directory, "challenge-unlock"));
        saved = store.Read(1)!.Data;
        var createdAt = saved.userReleaseConditions.Single(c => c.releaseConditionId == 90002).createdAt;
        check(createdAt > 0 && saved.userOneTimeBehaviors.Single().userId == 1 &&
            saved.userOneTimeBehaviors.Single().oneTimeBehaviorType == "challenge_live_character_force_release",
            "达到首次门槛后记录释放条件、时间和当前账号的一次行为");
        response = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "challenge-unlock/001.json")))!["response"]!;
        check(response["updatedResources"]!["userChallengeLiveSoloDecks"] == null &&
            saved.userChallengeLiveSoloStages.Single().point == 20,
            "首次解锁仅刷新条件和一次行为，不重建编队或阶段");
        check(response["updatedResources"]!["userReleaseConditions"]![0]!["userId"] == null &&
            saved.userReleaseConditions.Single(c => c.releaseConditionId == 90002).userId == 1,
            "释放条件响应按官方省略用户字段，存储仍保留归属");
        var repeatedFailed = false;
        try { await eligible.Send(unlockStep); }
        catch (ClientFailure) { repeatedFailed = true; }
        check(repeatedFailed && eligible.LastHttpStatus == 409 &&
            store.Read(1)!.Data.userReleaseConditions.Single(c => c.releaseConditionId == 90002).createdAt == createdAt,
            "重复首次解锁返回 409，保留原创建时间");
        await client.Send(new() { Operation = "system" });
        state = store.Read(1)!;
        state.Data.userChallengeLivePlayStatuses = [];
        store.Save(1, state);
        var start = new ScenarioStep { Operation = "challenge-live-start", Body = JsonNode.Parse(
            """{"characterId":1,"leader":1,"musicId":7,"musicDifficultyId":71,"musicVocalId":1,"isAuto":false}""")!.AsObject() };
        await ScenarioRunner.Run(client, new() { Steps = [start, start] }, Path.Combine(directory, "challenge-start"));
        var first = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "challenge-start/001.json")))!["response"]!;
        var second = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "challenge-start/002.json")))!["response"]!;
        var oldId = first["userChallengeLiveId"]!.GetValue<string>();
        var newId = second["userChallengeLiveId"]!.GetValue<string>();
        state = store.Read(1)!;
        check(oldId != newId && !state.Private.ChallengeLiveSessions.ContainsKey(oldId) &&
            state.Private.ChallengeLiveSessions.Single().Key == newId &&
            state.Data.userChallengeLivePlayStatuses.Single().userChallengeLiveId == newId &&
            state.Data.userChallengeLivePlayStatuses.Single().playCount == 0,
            "挑战重开替换私有会话和参与状态，不消耗次数");
        check(second["updatedResources"]!["userChallengeLivePlayStatuses"]![0]!["playEndAt"] == null &&
            second["skills"]![0]!["ingameCutinCharacterId"] == null && second["comboCutins"] == null &&
            second["skills"]!.AsArray().Count == 2 && state.Data.userChallengeLiveSoloStages.Single().point == 20,
            "挑战开局省略未发生的结束时间及切入字段，不修改阶段");
        state.Data.userChallengeLivePlayStatuses = [new() { characterId = 2, playCount = 1 }];
        store.Save(1, state);
        using var quota = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await quota.Send(new() { Operation = "system" });
        var quotaFailed = false;
        try { await quota.Send(start); }
        catch (ClientFailure) { quotaFailed = true; }
        check(quotaFailed && quota.LastHttpStatus == 409 && store.Read(1)!.Private.ChallengeLiveSessions.Single().Key == newId,
            "挑战次数跨角色合计，额度耗尽不覆盖已有会话");
        state = store.Read(1)!;
        state.Data.userChallengeLivePlayStatuses = [];
        store.Save(1, state);
        foreach (var (character, auto, status) in new[] { (99, false, 404), (1, true, 501) })
        {
            using var unsupported = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await unsupported.Send(new() { Operation = "system" });
            var body = start.Body!.DeepClone().AsObject();
            body["characterId"] = character;
            body["isAuto"] = auto;
            var rejectedStart = false;
            try { await unsupported.Send(new() { Operation = "challenge-live-start", Body = body }); }
            catch (ClientFailure) { rejectedStart = true; }
            check(rejectedStart && unsupported.LastHttpStatus == status &&
                store.Read(1)!.Private.ChallengeLiveSessions.Single().Key == newId &&
                store.Read(1)!.Data.userChallengeLivePlayStatuses.Length == 0,
                "缺少挑战编队或尚未支持的自动模式不产生会话及参与状态");
        }
        await client.Send(new() { Operation = "system" });
    }

    private static ScenarioStep Step(int leader, int? support = null, int character = 1) => new()
    {
        Operation = "challenge-deck-save", Args = new() { ["characterId"] = "1" },
        Body = new() { ["characterId"] = character, ["leader"] = leader, ["support1"] = support }
    };
}
