using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class StoryHttpChecks
{
    public static void WriteMaster(string directory)
    {
        // 隔离测试数值，不用于官方场景。
        File.WriteAllText(Path.Combine(directory, "releaseConditions.json"),
            """[{"id":700,"releaseConditionType":"card_level","releaseConditionTypeId":1,"releaseConditionTypeLevel":3}]""");
        File.WriteAllText(Path.Combine(directory, "cardEpisodes.json"),
            """[{"id":51,"cardId":1,"costs":[{"resourceType":"material","resourceId":1,"quantity":2}],"rewardResourceBoxIds":[51]},{"id":52,"cardId":1,"releaseConditionId":700}]""");
        File.WriteAllText(Path.Combine(directory, "unitStories.json"),
            """[{"chapters":[{"episodes":[{"id":61,"rewardResourceBoxIds":[51]}]}]}]""");
        File.WriteAllText(Path.Combine(directory, "specialStories.json"),
            """[{"episodes":[{"id":62,"rewardResourceBoxIds":[52]}]}]""");
        var path = Path.Combine(directory, "resourceBoxes.json");
        var boxes = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        boxes.Add(JsonNode.Parse("""{"id":51,"resourceBoxPurpose":"episode_reward","details":[{"resourceType":"coin","resourceQuantity":7}]}"""));
        boxes.Add(JsonNode.Parse("""{"id":52,"resourceBoxPurpose":"episode_reward","details":[{"resourceType":"material","resourceId":1,"resourceQuantity":3}]}"""));
        JsonFiles.Write(path, boxes);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userGamedata.coin = 0;
        state.Data.userMaterials = [new() { materialId = 1, quantity = 10 }];
        state.Data.userCards = [new() { cardId = 1, episodes = [new() { cardEpisodeId = 51, scenarioStatus = "unreleased" }] }];
        store.Save(1, state);
        var release = Step("story-release", """{"cardEpisodeReleaseCostType":"common_material"}""");
        release.Expect["/consumedResources/0/quantity"] = JsonValue.Create(2);
        var read = Step("story-read");
        read.Expect["/obtainedResources/0/quantity"] = JsonValue.Create(7);
        var repeat = Step("story-read");
        repeat.Expect["/obtainedResources"] = new JsonArray();
        var scenario = new Scenario { Steps =
        [
            release, read, repeat,
            Step("story-log", """{"noSkip":true,"useSkip":false,"autoFinish":false,"useAuto":false,"fastForward":false,"voice":false,"numPages":1,"continuousPlayStart":false,"playMusicVideo":false,"musicVocalId":0,"musicCategoryName":"","musicVideoNoSkip":false,"userStoryMusicPlays":[]}"""),
            new() { Operation = "story-recommend" },
            new() { Operation = "friend-story-favorites", Args = new() { ["storyType"] = "unit_story" } }
        ] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        var output = Path.Combine(directory, "story");
        await ScenarioRunner.Run(client, scenario, output);
        var saved = store.Read(1)!.Data;
        check(saved.userMaterials.Single().quantity == 8 && saved.userGamedata.coin == 7,
            "剧情解锁扣除材料，阅读奖励只领取一次");
        var episode = saved.userCards.Single().episodes.Single();
        check(episode.scenarioStatus == "already_read" && episode.isNotSkipped,
            "剧情日志通过真实 HTTP 保存未跳过状态");
        var cost = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!;
        check(cost["after"]!["userCards"]![0]!["episodes"]![0]!["scenarioStatus"]!.GetValue<string>() == "released",
            "解锁响应后的完整状态确认已解锁");
        check(cost["stateChanges"]!.AsArray().Count > 0 && cost["response"]!["updatedResources"]!["userBeginnerMissionBehavior"] == null,
            "剧情状态变化留档并保留任务字段排除规则");
        var log = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "004.json")))!;
        check(log["response"]!["userObtainResourceResults"] is JsonArray,
            "剧情日志读取资源结果字段");
        var recommend = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "005.json")))!;
        var friends = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "006.json")))!;
        check(recommend["response"]!["userStoryRecommends"] is JsonArray &&
            friends["response"]!["friendStoryFavoriteStatuses"] is JsonArray,
            "推荐和好友收藏占位接口可读取，不验证占位数据为官方规则");
        var releaseAgain = Step("story-release", """{"cardEpisodeReleaseCostType":"common_material"}""");
        releaseAgain.Expect["/consumedResources"] = new JsonArray();
        await ScenarioRunner.Run(client, new() { Steps = [releaseAgain, repeat] }, Path.Combine(output, "repeat-release"));
        var releasedAgain = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "repeat-release/001.json")))!;
        var unchangedEpisode = releasedAgain["after"]!["userCards"]![0]!["episodes"]![0]!;
        check(unchangedEpisode["scenarioStatus"]!.GetValue<string>() == "already_read" &&
            unchangedEpisode["isNotSkipped"]!.GetValue<bool>(), "重复解锁保持已读与未跳过状态");
        check(store.Read(1)!.Data.userGamedata.coin == 7 && store.Read(1)!.Data.userMaterials.Single().quantity == 8,
            "已读剧情重复解锁再阅读不重复扣材或发奖");
        state = store.Read(1)!;
        state.Data.userUnitEpisodeStatuses = [new() { episodeId = 61, status = "released" }];
        state.Data.userSpecialEpisodeStatuses = [new() { episodeId = 62, status = "released" }];
        store.Save(1, state);
        var rewards = new Scenario { Steps =
        [
            Read("unit_story", "61", 7), Read("special_story", "62", 3),
            Read("unit_story", "61", null), Read("special_story", "62", null)
        ] };
        ScenarioRunner.Validate(rewards, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, rewards, Path.Combine(output, "rewards"));
        var repeatedUnit = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "rewards/003.json")))!;
        check(repeatedUnit["httpStatus"]!.GetValue<int>() == 204 && repeatedUnit["response"]!.AsObject().Count == 0,
            "已读主线返回 204 空响应并继续完成后续请求");
        state = store.Read(1)!;
        state.Data.refreshableTypes = ["userCards"];
        store.Save(1, state);
        await client.Send(Read("unit_story", "61", null));
        check(store.Read(1)!.Data.refreshableTypes.SequenceEqual(["userCards"]),
            "重复阅读的只读检查不消耗待刷新字段");
        saved = store.Read(1)!.Data;
        check(saved.userGamedata.coin == 14 && saved.userMaterials.Single().quantity == 11,
            "主线与特殊剧情按各自 master 奖励盒发奖且重复请求不重复发放");
        check(saved.userUnitEpisodeStatuses.Single().status == "already_read" &&
            saved.userSpecialEpisodeStatuses.Single().status == "already_read",
            "主线与特殊剧情首读状态通过 HTTP 持久化");
    }

    private static ScenarioStep Read(string type, string id, int? quantity) => new()
    {
        Operation = "story-read", Args = new() { ["storyType"] = type, ["episodeId"] = id },
        Expect = quantity is { } value
            ? new() { ["/obtainedResources/0/quantity"] = JsonValue.Create(value) }
            : type == "unit_story" ? new() : new() { ["/obtainedResources"] = new JsonArray() }
    };

    private static ScenarioStep Step(string operation, string? body = null) => new()
    {
        Operation = operation,
        Args = new() { ["storyType"] = "card_story", ["episodeId"] = "51" },
        Body = body == null ? null : JsonNode.Parse(body)!.AsObject()
    };
}
