extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using game::Sekai.MultiLive;
using MessagePack;
using PrivateSekai.Client;
using PrivateSekai.Client.Realtime;
using PrivateSekai.Protocol;

internal static class MultiLivePerformanceChecks
{
    public static void Run(Action<bool, string> check)
    {
        var input = new UserMultiLiveClearScoreRequest
        {
            score = 100, perfectCount = 8, greatCount = 1, goodCount = 1, maxCombo = 9, life = 1000, tapCount = 10
        };
        var owner = MultiLivePerformance.Score(input, 11, 372);
        var guest = MultiLivePerformance.Score(input, 22, 372);
        guest.score = 200;
        check(owner.userId == 11 && owner.musicDifficultyId == 372 && input.userId == 0 && input.musicDifficultyId == 0,
            "成绩副本只填本次真实账号与难度，不修改输入模板");
        var packed = JsonNode.Parse(MessagePackSerializer.ConvertToJson(DumpSerializer.Serialize(owner)))!.AsObject();
        check(packed.Count == 11 && !packed.ContainsKey("fastCount") && !packed.ContainsKey("lateCount") && !packed.ContainsKey("flickCount"),
            "多人个人成绩使用原11字段契约，不发送IgnoreMember统计");
        var ready = MultiLivePerformance.Ready(owner);
        var decoded = SyncPropertyEncoding.ReadObject<UserMultiLiveClearScoreRequest>(ready.values[10]);
        check(ready.values.Keys.Order().SequenceEqual(new[] { 4, 10 }) && ready.isRSend == 1
            && SyncPropertyEncoding.ReadInt32(ready.values[4]) == (int)PlayerStatus.WaitResult
            && decoded.userId == 11 && decoded.score == 100, "等待结算以玩家RESULT对象和状态13可靠同步，保留成绩身份");
        var scores = new Dictionary<long, UserMultiLiveClearScoreRequest> { [11] = owner, [22] = guest };
        var result = MultiLivePerformance.Result([22, 11], scores);
        check(result.score1.userId == 22 && result.score2.userId == 11 && result.score3 == null
            && result.score4 == null && result.score5 == null && result.totalScore == 300,
            "汇总使用准备时成员槽位，不补造空槽玩家，总分为实际同步分数之和");
        check(!result.superFeverFlg && !result.isMirrored && result.disconnectUserIds.Count == 0
            && result.ingameCutinCharacterArchiveVoiceGroupIds == null && result.musicCategoryName == "image"
            && result.privateRoomSettings.liveRuleType == "normal", "普通无断连用例无Fever、镜像或未播放的语音奖励");
        Reject(() => MultiLivePerformance.Score(new() { userId = 33 }, 11, 372));
        Reject(() => MultiLivePerformance.Score(new() { musicDifficultyId = 1 }, 11, 372));
        Reject(() => MultiLivePerformance.Score(new() { score = -1 }, 11, 372));
        Reject(() => MultiLivePerformance.Result([11, 33], scores));
        Reject(() => MultiLivePerformance.Result([11, 11], scores));
        Reject(() => MultiLivePerformance.Result([11, 22], new Dictionary<long, UserMultiLiveClearScoreRequest> { [11] = guest, [22] = owner }));
        var operation = Operations.All["multi-live-clear"];
        check(operation.Method == "PUT" && operation.RequiredResponseField == "totalScore" && !operation.Snapshot
            && Operations.Path(operation, new() { Args = new() { ["liveId"] = "fixture-live" } }, 11)
                == "/api/user/11/multi-live/fixture-live", "结算是当前玩家的PUT接口，成功判据包含总分");
        var body = Operations.EncodeBody(operation, JsonSerializer.SerializeToNode(result, DumpJson.Options)!.AsObject())!;
        check(DumpSerializer.Deserialize<UserMultiLiveClearRequest>(body).score1.score == 200,
            "多人汇总请求按dump契约编码，嵌套成绩不丢失");
        var redacted = new Redactor().Clean(new JsonObject
        {
            ["activeUserIds"] = new JsonArray(11, 22), ["disconnectUserIds"] = new JsonArray(33),
            ["result"] = JsonSerializer.SerializeToNode(result, DumpJson.Options)
        })!.AsObject();
        check(redacted["activeUserIds"]!.AsArray().All(id => id!.GetValue<string>() == "<redacted>")
            && redacted["disconnectUserIds"]![0]!.GetValue<string>() == "<redacted>"
            && redacted["result"]!["score1"]!["userId"]!.GetValue<string>() == "<redacted>",
            "结算记录隐藏活动成员、断连成员及嵌套成绩账号");
        var path = Path.Combine(AppContext.BaseDirectory, "performance-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{"durationMilliseconds":180000,"ownerScore":{"score":100},"guestScore":{"score":200}}""");
        check(MultiLivePerformance.Read(path).DurationMilliseconds == 180000
            && MultiLivePerformance.Read(path).OwnerBoostCount == 0 && MultiLivePerformance.Read(path).GuestBoostCount == 0
            && MultiLivePerformance.Read(path).GuestDifficulty == null,
            "成绩用例明确等待时长，旧配置默认双方零体力");
        File.WriteAllText(path, """{"durationMilliseconds":180000,"ownerBoostCount":1,"guestBoostCount":2,"ownerScore":{},"guestScore":{}}""");
        check(MultiLivePerformance.Read(path).OwnerBoostCount == 1 && MultiLivePerformance.Read(path).GuestBoostCount == 2,
            "演出用例分别保存双方体力选择，旧配置默认零体力");
        File.WriteAllText(path, """{"durationMilliseconds":1,"guestDifficulty":"normal","ownerScore":{},"guestScore":{}}""");
        check(MultiLivePerformance.Read(path).GuestDifficulty == "normal", "成员可独立选择难度，未指定时沿用房主选择");
        File.WriteAllText(path, """{"durationMilliseconds":1,"ownerBoostCount":-1,"ownerScore":{},"guestScore":{}}""");
        Reject(() => MultiLivePerformance.Read(path));
        File.WriteAllText(path, """{"durationMilliseconds":0,"ownerScore":{},"guestScore":{}}""");
        Reject(() => MultiLivePerformance.Read(path));
        File.WriteAllText(path, """{"durationMilliseconds":1,"ownerScore":{"fastCount":1},"guestScore":{}}""");
        var unknownRejected = false;
        try { MultiLivePerformance.Read(path); }
        catch (JsonException) { unknownRejected = true; }
        check(unknownRejected, "不支持的统计字段不能悄悄进入成绩模板");
        void Reject(Action action)
        {
            var rejected = false;
            try { action(); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "无效账号、成绩、成员或等待配置在业务请求前拒绝");
        }
    }
}
