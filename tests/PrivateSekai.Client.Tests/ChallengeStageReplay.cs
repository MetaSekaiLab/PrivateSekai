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
        ChallengeExperienceReplay.ImportMaster(source, destination);
        foreach (var table in new[] { "challengeLiveStages", "resourceBoxes", "levels", "characterRanks", "musicDifficulties", "playLevelScores", "configs", "liveMissions", "liveMissionPeriods", "beginnerMissionV2s", "characterMissionV2s", "characterMissionV2ParameterGroups", "birthdayParties", "events", "eventItems", "eventBreakTimes", "releaseConditions" })
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
        var experienceBefore = official["before"]!.DeepClone();
        string[] experienceFields = ["userGamedata", "userCards", "userBoost", "userChargedCurrency"];
        foreach (var field in experienceFields)
        {
            if (experienceBefore[field] is JsonObject obj && obj["userId"] != null) obj["userId"] = 1;
            if (experienceBefore[field] is JsonArray rows)
                foreach (var row in rows)
                    if (row?["userId"] != null) row["userId"] = 1;
        }
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => experienceFields.Contains((string)m.Key)))
            if (experienceBefore[(string)member.Key] is { } value)
                member.Set(state.Data, JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options));
        state.Data.userChallengeLiveSoloStages = JsonSerializer.Deserialize<UserChallengeLiveSoloStage[]>(
            official["before"]!["userChallengeLiveSoloStages"]!.ToJsonString(), DumpJson.Options)!;
        state.Data.userCharacters = JsonSerializer.Deserialize<UserCharacter[]>(
            official["before"]!["userCharacters"]!.ToJsonString(), DumpJson.Options)!;
        state.Data.userMaterials = official["before"]!["userMaterials"]!.Deserialize<UserMaterial[]>(DumpJson.Options);
        state.Data.userEvents = official["before"]!["userEvents"]!.Deserialize<UserEvent[]>(DumpJson.Options);
        state.Data.userEventItems = official["before"]!["userEventItems"]!.Deserialize<UserEventItem[]>(DumpJson.Options);
        state.Data.userEventBreakTime = official["before"]?["userEventBreakTime"]?.Deserialize<UserEventBreakTime>(DumpJson.Options);
        state.Data.userReleaseConditions = official["before"]!["userReleaseConditions"]!.Deserialize<UserReleaseCondition[]>(DumpJson.Options);
        foreach (var condition in state.Data.userReleaseConditions ?? []) condition.userId = 1;
        state.Data.userColorfulPassV2 = official["before"]?["userColorfulPassV2"]?.Deserialize<UserColorfulPassV2>(DumpJson.Options);
        state.Data.userLiveMissions = official["before"]!["userLiveMissions"]!.Deserialize<UserLiveMission[]>(DumpJson.Options);
        state.Data.userMissionStatuses = official["before"]!["userMissionStatuses"]!.Deserialize<UserMissionStatus[]>(DumpJson.Options);
        state.Data.userBeginnerMissionV2s = official["before"]!["userBeginnerMissionV2s"]!.Deserialize<UserBeginnerMissionV2[]>(DumpJson.Options);
        state.Data.userFriends = official["before"]?["userFriends"]?.AsArray()
            .Select((friend, index) => new UserFriend
            {
                opponentUserId = index + 2, friendStatus = friend!["friendStatus"]!.GetValue<string>()
            }).ToArray();
        foreach (var mission in state.Data.userLiveMissions ?? []) mission.userId = 1;
        foreach (var status in state.Data.userMissionStatuses ?? []) status.userId = 1;
        state.Data.userChallengeLivePlayStatuses = official["before"]!["userChallengeLivePlayStatuses"]!.Deserialize<UserChallengeLivePlayStatus[]>(DumpJson.Options);
        state.Data.userCharacterMissions = official["before"]!["userCharacterMissionV2s"]!.Deserialize<UserCharacterMissionV2[]>(DumpJson.Options);
        state.Data.userCharacterLiveUsageCounts = official["before"]!["userCharacterLiveUsageCounts"]!.Deserialize<UserCharacterLiveUsageCount[]>(DumpJson.Options);
        var characterStatuses = official["before"]!["userCharacterMissionV2Statuses"]!.DeepClone();
        foreach (var status in characterStatuses.AsArray()) status!["userId"] = 1;
        state.Data.userCharacterMissionStatuses = characterStatuses.Deserialize<UserCharacterMissionV2Status[]>(DumpJson.Options);
        var startNode = official["before"]!["userChallengeLiveSoloDecks"]!.AsArray()
            .Single(d => d!["characterId"]!.GetValue<int>() == characterId)!.DeepClone();
        foreach (var field in new[] { "musicId", "musicDifficultyId", "musicVocalId", "isAuto" })
            startNode[field] = playStatus[field]!.DeepClone();
        var start = startNode.Deserialize<UserChallengeLiveStartRequest>(DumpJson.Options)!;
        state.Private.ChallengeLiveSessions[sessionId] = start;
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
        LimitedTermScoreRankRewardResult[] birthdayRewards = [];
        JsonObject eventResult = new();
        JsonObject experienceResult = new();
        JsonNode? responseMissions = null;
        var clear = official["request"]!.Deserialize<UserChallengeLiveClearRequest>(DumpJson.Options)!;
        operations.Execute(1, () =>
        {
            var experience = service.GainExperience(start, clear);
            experienceResult = new JsonObject
            {
                ["userExpResult"] = JsonSerializer.SerializeToNode(experience.Player, DumpJson.Options),
                ["deckCardExpResults"] = JsonSerializer.SerializeToNode(experience.Cards, DumpJson.Options),
                ["playerRankRewards"] = JsonSerializer.SerializeToNode(experience.Rewards, DumpJson.Options)
            };
            var result = service.AdvanceStage(start, clear);
            service.UpdateMissions(start, clear);
            birthdayRewards = service.GrantBirthdayRewards(start, clear, playStatus["playStartAt"]!.GetValue<long>());
            var points = service.GainEventPoint(start, clear, playStatus["playStartAt"]!.GetValue<long>());
            eventResult = new JsonObject
            {
                ["beforeEventPoint"] = points.BeforePoint, ["afterEventPoint"] = points.AfterPoint,
                ["beforeEventItemQuantity"] = points.BeforeItems, ["afterEventItemQuantity"] = points.AfterItems
            };
            if (!service.CompletePlay(sessionId, clear)) throw new InvalidOperationException("挑战会话未完成。");
            actual = JsonSerializer.SerializeToNode(result, DumpJson.Options)!.AsObject();
            responseMissions = MissionProjection(user.BuildRefresh());
            return user.BuildRefresh();
        });
        var officialRefresh = official["response"]!["updatedResources"]!;
        var responseMissionDifferences = Comparison.Diff(MissionProjection(new SuiteUser
        {
            userLiveMissions = officialRefresh["userLiveMissions"]?.Deserialize<UserLiveMission[]>(DumpJson.Options),
            userBeginnerMissionV2s = officialRefresh["userBeginnerMissionV2s"]?.Deserialize<UserBeginnerMissionV2[]>(DumpJson.Options),
            userMissionStatuses = officialRefresh["userMissionStatuses"]?.Deserialize<UserMissionStatus[]>(DumpJson.Options)
        }), responseMissions);
        // 官方 after 是一次独立完整Suite，需执行其刷新副作用后再比较持久状态。
        operations.Execute(1, () => user.BuildSuite());
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
        var expectedExperience = new JsonObject
        {
            ["userExpResult"] = JsonSerializer.SerializeToNode(official["response"]!["userExpResult"]!.Deserialize<UpdateExpResult>(DumpJson.Options), DumpJson.Options),
            ["deckCardExpResults"] = JsonSerializer.SerializeToNode(official["response"]!["deckCardExpResults"]!.Deserialize<DeckCardUpdateExpResult[]>(DumpJson.Options), DumpJson.Options),
            ["playerRankRewards"] = JsonSerializer.SerializeToNode(official["response"]!["playerRankRewards"]!.Deserialize<UserResource[]>(DumpJson.Options), DumpJson.Options)
        };
        var experienceDifferences = Comparison.Diff(expectedExperience, experienceResult);
        var expectedCards = official["after"]!["userCards"]!.DeepClone();
        foreach (var card in expectedCards.AsArray())
            if (card?["userId"] != null) card["userId"] = 1;
        var cardDifferences = Comparison.Diff(JsonSerializer.SerializeToNode(expectedCards.Deserialize<UserCard[]>(DumpJson.Options), DumpJson.Options),
            JsonSerializer.SerializeToNode(saved.userCards, DumpJson.Options));
        var expectedPlayer = official["after"]!["userGamedata"]!;
        if (saved.userGamedata.totalExp != expectedPlayer["totalExp"]!.GetValue<int>() ||
            saved.userGamedata.exp != expectedPlayer["exp"]!.GetValue<int>() || saved.userGamedata.rank != expectedPlayer["rank"]!.GetValue<int>())
            throw new InvalidOperationException("挑战玩家经验持久状态不一致。");
        var expectedEventResult = new JsonObject(eventResult.Select(p =>
            KeyValuePair.Create(p.Key, official["response"]![p.Key]?.DeepClone())));
        var eventDifferences = Comparison.Diff(expectedEventResult, eventResult);
        var expectedConditions = official["after"]!["userReleaseConditions"]!.Deserialize<UserReleaseCondition[]>(DumpJson.Options)!;
        foreach (var condition in expectedConditions) condition.userId = 1;
        var eventStateDifferences = Comparison.Diff(JsonSerializer.SerializeToNode(new
        {
            events = official["after"]!["userEvents"]!.Deserialize<UserEvent[]>(DumpJson.Options),
            items = official["after"]!["userEventItems"]!.Deserialize<UserEventItem[]>(DumpJson.Options),
            conditions = expectedConditions
        }, DumpJson.Options), JsonSerializer.SerializeToNode(new
        {
            events = saved.userEvents, items = saved.userEventItems, conditions = saved.userReleaseConditions
        }, DumpJson.Options));
        var birthdayDifferences = Comparison.Diff(JsonSerializer.SerializeToNode(
            official["response"]!["limitedTermScoreRankRewards"]!.Deserialize<LimitedTermScoreRankRewardResult[]>(DumpJson.Options), DumpJson.Options),
            JsonSerializer.SerializeToNode(birthdayRewards, DumpJson.Options));
        foreach (var reward in birthdayRewards.SelectMany(r => r.obtainedRewards))
        {
            var expectedQuantity = official["after"]!["userMaterials"]!.AsArray()
                .Single(m => m!["materialId"]!.GetValue<int>() == reward.resourceId)!["quantity"]!.GetValue<int>();
            if (saved.userMaterials.Single(m => m.materialId == reward.resourceId).quantity != expectedQuantity)
                throw new InvalidOperationException("生日材料库存与官方不一致。");
        }
        var playDifferences = Comparison.Diff(JsonSerializer.SerializeToNode(
            official["after"]!["userChallengeLivePlayStatuses"]!.Deserialize<UserChallengeLivePlayStatus[]>(DumpJson.Options), DumpJson.Options),
            JsonSerializer.SerializeToNode(saved.userChallengeLivePlayStatuses, DumpJson.Options));
        var expectedCharacterStatuses = official["after"]!["userCharacterMissionV2Statuses"]!.DeepClone();
        foreach (var status in expectedCharacterStatuses.AsArray()) status!["userId"] = 1;
        var characterMissionDifferences = Comparison.Diff(JsonSerializer.SerializeToNode(new
        {
            progress = official["after"]!["userCharacterMissionV2s"]!.Deserialize<UserCharacterMissionV2[]>(DumpJson.Options),
            statuses = expectedCharacterStatuses.Deserialize<UserCharacterMissionV2Status[]>(DumpJson.Options),
            usage = official["after"]!["userCharacterLiveUsageCounts"]!.Deserialize<UserCharacterLiveUsageCount[]>(DumpJson.Options)
        }, DumpJson.Options), JsonSerializer.SerializeToNode(new
        {
            progress = saved.userCharacterMissions, statuses = saved.userCharacterMissionStatuses, usage = saved.userCharacterLiveUsageCounts
        }, DumpJson.Options));
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
            scope = "玩家及卡牌经验、评分、点数、阶段、角色升级、任务响应及后续Suite、完成状态、生日奖励及活动的业务重放；不验证跨期演出或完整 HTTP 结算",
            scoreRank,
            expected = projected, actual,
            resultDifferences = differences, stageDifferences, characterDifferences, missionDifferences, playDifferences, characterMissionDifferences, birthdayDifferences, eventDifferences, eventStateDifferences,
            experienceDifferences, cardDifferences, responseMissionDifferences,
            actualStages = after
        });
        if (differences.Count != 0 || stageDifferences.Count != 0 || characterDifferences.Count != 0 || missionDifferences.Count != 0 ||
            playDifferences.Count != 0 || characterMissionDifferences.Count != 0 || birthdayDifferences.Count != 0 ||
            eventDifferences.Count != 0 || eventStateDifferences.Count != 0 || experienceDifferences.Count != 0 || cardDifferences.Count != 0 ||
            responseMissionDifferences.Count != 0)
            throw new InvalidOperationException("挑战阶段与官方样本存在差异，见重放报告。");
        if (store.Read(1)!.Private.ChallengeLiveSessions.ContainsKey(sessionId))
            throw new InvalidOperationException("已完成的私有挑战会话未清理。");
        var persisted = JsonSerializer.Serialize(saved, DumpJson.Options);
        operations.Execute(1, () =>
        {
            if (service.CompletePlay(sessionId, clear)) throw new InvalidOperationException("重复挑战结算未拒绝。");
            return null;
        });
        if (JsonSerializer.Serialize(store.Read(1)!.Data, DumpJson.Options) != persisted)
            throw new InvalidOperationException("重复挑战结算改变用户状态。");
        Console.WriteLine("挑战阶段业务重放通过；不代表完整结算接口通过。");
    }

    private static JsonNode? MissionProjection(SuiteUser value)
    {
        foreach (var mission in value.userLiveMissions ?? []) mission.userId = 1;
        foreach (var status in value.userMissionStatuses ?? []) status.userId = 1;
        return JsonSerializer.SerializeToNode(new
        {
            live = value.userLiveMissions, beginner = value.userBeginnerMissionV2s, statuses = value.userMissionStatuses
        }, DumpJson.Options);
    }
}
