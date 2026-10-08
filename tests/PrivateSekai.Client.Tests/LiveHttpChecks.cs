using System.Text.Json.Nodes;
using PrivateSekai.Config;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class LiveHttpChecks
{
    public static void WriteBoostMaster(string directory)
    {
        var path = Path.Combine(directory, "configs.json");
        var configs = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        configs.Add(JsonNode.Parse("""{"configKey":"boost_recovery_max_count","value":"25"}"""));
        configs.Add(JsonNode.Parse("""{"configKey":"boost_recovery_second","value":"1800"}"""));
        JsonFiles.Write(path, configs);
    }

    public static void WriteMaster(string directory)
    {
        // 仅用于测试结算链路的虚构小型 master。
        var tables = new Dictionary<string, string>
        {
            ["musicDifficulties"] = """[{"id":71,"musicId":7,"musicDifficulty":"easy","playLevel":6,"totalNoteCount":10}]""",
            ["musicCategories"] = """[{"musicId":7,"musicCategoryName":"original"}]""",
            ["limitedTimeMusics"] = "[]",
            ["playLevelScores"] = """[{"liveType":"solo","playLevel":6,"s":500,"a":400,"b":300,"c":100}]""",
            ["boosts"] = """[{"id":1,"costBoost":1,"rewardRate":2,"livePointRate":3}]""",
            ["liveMissionPeriods"] = """[{"id":1,"startAt":0,"endAt":4102444800000}]""",
            ["liveMissions"] = """[{"id":1,"liveMissionPeriodId":1,"liveMissionType":"free","requirement":3}]""",
            ["honorMissions"] = """[{"id":10001,"honorMissionType":"easy_full_combo","requirement":1}]""",
            ["musicAchievements"] = "[]"
        };
        foreach (var (table, json) in tables) File.WriteAllText(Path.Combine(directory, table + ".json"), json);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var scenario = new Scenario { Steps =
        [
            new() { Operation = "system" },
            new() { Operation = "live-start", Body = JsonNode.Parse("""{"musicId":7,"musicDifficultyId":71,"musicCategoryName":"original","deckId":1,"boostCount":1,"isAuto":false}""")!.AsObject() },
            new() { Operation = "live-clear", UseLiveSession = true, DelayBeforeMs = 1,
                Body = JsonNode.Parse("""{"score":150,"perfectCount":10,"maxCombo":10,"life":1000,"ingameCutinCharacterArchiveVoiceGroupIds":[4]}""")!.AsObject(),
                Expect = new() { ["/fullPerfectFlg"] = JsonValue.Create(true), ["/score"] = JsonValue.Create(150) } },
            new() { Operation = "live-voice", UseLiveSession = true,
                Body = JsonNode.Parse("""{"liveResultCharacterArchiveVoiceGroupId":5,"liveType":"solo"}""")!.AsObject() }
        ] };
        ScenarioRunner.Validate(scenario, [config], new HashSet<string>());
        var firstState = store.Read(1)!;
        var liveDeck = firstState.Data.userDecks.Single(d => d.deckId == 1);
        liveDeck.leader = liveDeck.member1 = 1;
        liveDeck.subLeader = liveDeck.member2 = liveDeck.member3 = liveDeck.member4 = liveDeck.member5 = 0;
        firstState.Data.userBoost = new() { current = 3, recoveryAt = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        firstState.Data.userMusicResults = [];
        firstState.Data.userHonorMissions = [];
        firstState.Data.userLiveMissions = [];
        firstState.Data.userMissionStatuses = [];
        firstState.Data.userBeginnerMissionV2s = [];
        firstState.Data.userLiveCharacterArchiveVoice = new() { characterArchiveVoiceGroupIds = [] };
        store.Save(1, firstState);
        var secondState = store.Read(1)!;
        secondState.Data.userRegistration.userId = 2;
        secondState.Data.userGamedata.userId = 2;
        store.Save(2, secondState);
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "live-first"));
        using var second = new ProtocolClient(new() { BaseUrl = config.BaseUrl, UserId = 2, RequireRotatingToken = true },
            directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await ScenarioRunner.Run(second, scenario, Path.Combine(directory, "live-second"));
        foreach (var id in new long[] { 1, 2 })
        {
            var saved = store.Read(id)!;
            check(saved.Private.UserLiveSessions.Count == 0 && saved.Data.userMusicResults.Single().highScore == 150,
                "Live 真实 HTTP 结算保存成绩并移除各自会话");
            check(saved.Data.userBoost.current == 2 && saved.Data.userLiveMissions.Single().progress == 3,
                "Live 结算保存体力消耗和任务进度");
            check(saved.Data.userLiveMissions.Single().achievedMissionIds.Length == 0 &&
                saved.Data.userMissionStatuses.Single(s => s.missionType == "live_mission").missionStatus == "achieved",
                "Live 达成状态持久化，结算提示 ID 不进入存档");
            var honor = saved.Data.userHonorMissions.Single(m => m.honorMissionType == "easy_full_combo");
            check(honor.progress == 1 && honor.achievedMissionIds.Length == 0,
                "Easy FC 进度持久化，新增称号任务提示不进入存档");
            check(saved.Data.userBeginnerMissionV2s.Single().progress == 1 && !saved.Data.userBeginnerMissionV2s.Single().isNewAchieved,
                "新手演出任务保存进度但不保存本次达成提示");
            check(saved.Data.userLiveCharacterArchiveVoice.characterArchiveVoiceGroupIds.Order().SequenceEqual(new[] { 4, 5 }),
                "结算与结果页语音接口共同更新语音状态");
        }
        var first = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "live-first/002.json")))!;
        var other = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "live-second/002.json")))!;
        var firstId = first["response"]!["userLiveId"]!.GetValue<string>();
        var secondId = other["response"]!["userLiveId"]!.GetValue<string>();
        check(firstId != secondId, "两份客户端获得不同 Live ID");
        foreach (var (name, id) in new[] { ("live-first", firstId), ("live-second", secondId) })
        {
            var clear = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, name, "003.json")))!;
            var voice = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, name, "004.json")))!;
            check(clear["response"]!["scoreRank"]!.GetValue<string>() == "rank_c",
                "结算响应包含 master 阈值计算的评分");
            var mission = clear["response"]!["updatedResources"]!["userLiveMissions"]![0]!;
            var honorMission = clear["response"]!["updatedResources"]!["userHonorMissions"]!.AsArray()
                .Single(m => m!["honorMissionType"]!.GetValue<string>() == "easy_full_combo")!;
            check(honorMission["achievedMissionIds"]!.AsArray().Single()!.GetValue<int>() == 10001 && honorMission["userId"] == null,
                "Easy FC 达成只在本次 HTTP 响应提示且省略账号字段");
            check(mission["achievedMissionIds"]!.AsArray().Single()!.GetValue<int>() == 1 && mission["userId"] == null,
                "Live HTTP 结算提示本次新达成 ID 并省略用户 ID");
            check(clear["response"]!["updatedResources"]!["userBeginnerMissionV2s"]![0]!["isNewAchieved"]!.GetValue<bool>(),
                "新手演出任务仅在达成的结算响应中提示");
            check(clear["delayBeforeMs"]!.GetValue<int>() == 1 && clear["status"]!.GetValue<string>() == "completed",
                "等待后的步骤保留会话引用并记录完成状态");
            check(clear["args"]!["userLiveId"]!.GetValue<string>() == id && voice["request"]!["userLiveId"]!.GetValue<string>() == id,
                "路径及 body 均引用当前客户端开局响应");
        }
        check(!scenario.Steps[2].Args.ContainsKey("userLiveId") && !scenario.Steps[3].Body!.ContainsKey("userLiveId"),
            "重复运行不把第一端会话 ID 写回场景");
        // 测试 HTTP 服务使用单条 token 链，恢复第一客户端的链后继续其他检查。
        await client.Send(new() { Operation = "system" });
        var judgments = new Scenario { Steps =
        [
            new() { Operation = "live-start", Body = JsonNode.Parse("""{"musicId":7,"musicDifficultyId":71,"musicCategoryName":"original","deckId":1,"boostCount":1,"isAuto":false}""")!.AsObject() },
            new() { Operation = "live-clear", UseLiveSession = true,
                Body = JsonNode.Parse("""{"score":150,"perfectCount":8,"goodCount":2,"maxCombo":8,"life":1000}""")!.AsObject() },
            new() { Operation = "live-start", Body = JsonNode.Parse("""{"musicId":7,"musicDifficultyId":71,"musicCategoryName":"original","deckId":1,"boostCount":1,"isAuto":false}""")!.AsObject() },
            new() { Operation = "live-clear", UseLiveSession = true,
                Body = JsonNode.Parse("""{"score":150,"perfectCount":8,"greatCount":2,"maxCombo":10,"life":1000}""")!.AsObject() }
        ] };
        var judgmentDirectory = Path.Combine(directory, "live-judgments");
        await ScenarioRunner.Run(client, judgments, judgmentDirectory);
        var good = JsonNode.Parse(File.ReadAllText(Path.Combine(judgmentDirectory, "002.json")))!["response"]!;
        var great = JsonNode.Parse(File.ReadAllText(Path.Combine(judgmentDirectory, "004.json")))!["response"]!;
        check(!good["fullComboFlg"]!.GetValue<bool>() && !good["fullPerfectFlg"]!.GetValue<bool>(),
            "含 GOOD 且没有 BAD/MISS 的结算不算全连");
        check(great["fullComboFlg"]!.GetValue<bool>() && !great["fullPerfectFlg"]!.GetValue<bool>(),
            "GREAT 保留全连但不算 AP");
        check(store.Read(1)!.Data.userHonorMissions.Single(m => m.honorMissionType == "easy_full_combo").progress == 1 &&
            great["updatedResources"]!["userHonorMissions"]!.AsArray()
                .Single(m => m!["honorMissionType"]!.GetValue<string>() == "easy_full_combo")!["achievedMissionIds"]!.AsArray().Count == 0,
            "同曲再次 FC 不重复累计称号进度或提示达成");
        check(store.Read(1)!.Data.userHonorMissions.Single(m => m.honorMissionType == "clear_live").progress == 3,
            "同曲成功演出仍累计通关次数，与 FC 去重分开");
        check(good["updatedResources"]!["userLiveMissions"]![0]!["achievedMissionIds"]!.AsArray().Count == 0 &&
            good["updatedResources"]!["userMissionStatuses"] == null,
            "已达成任务后续结算不重复提示或刷新任务状态");
        check(good["updatedResources"]!["userBeginnerMissionV2s"] == null && store.Read(1)!.Data.userBeginnerMissionV2s.Single().progress == 1,
            "新手演出任务达到门槛后不再增长或刷新");
        check(store.Read(1)!.Data.userMusicResults.Single().playResult == "full_perfect" &&
            good["updatedResources"]?["userMusicResults"] == null && great["updatedResources"]?["userMusicResults"] == null,
            "当前局判定不会降低历史 AP，也不重复刷新未变成绩");
    }
}
