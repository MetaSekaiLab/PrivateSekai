extern alias game;

using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using game::CP.Realtime;
using game::Sekai;
using game::Sekai.MultiLive;
using PrivateSekai.Client;
using PrivateSekai.Client.Realtime;
using PrivateSekai.Protocol;

internal static class MultiLiveFlowChecks
{
    public static void Run(Action<bool, string> check)
    {
        var selected = MultiLiveFlow.SelectMusic(37);
        check(selected.isRSend == 1 && selected.values.Keys.Order().SequenceEqual(new[] { 4, 8, 9 })
            && SyncPropertyEncoding.ReadInt32(selected.values[4]) == (int)PlayerStatus.FixedMusic
            && SyncPropertyEncoding.ReadInt32(selected.values[8]) == 37
            && SyncPropertyEncoding.ReadInt32(selected.values[9]) == 0, "选曲携带曲目、非委托标志与确定状态，不混入房间属性");
        var ready = MultiLiveFlow.Ready("hard");
        check(ready.values.Keys.Order().SequenceEqual(new[] { 4, 7, 11 })
            && SyncPropertyEncoding.ReadString(ready.values[7]) == "hard"
            && SyncPropertyEncoding.ReadString(ready.values[11]) == ""
            && SyncPropertyEncoding.ReadInt32(ready.values[4]) == (int)PlayerStatus.ReadyToLive,
            "准备保留难度和空自制谱标识，按玩家状态6同步");
        check(!MultiLiveFlow.Matches(selected, ready)
            && MultiLiveFlow.Matches(selected, MultiLiveFlow.Status(PlayerStatus.FixedMusic)),
            "匹配推送要求全部目标字段，单一状态不能冒充难度准备完成");
        var owner = new RoomSyncPlayer { userId = "11", index = 20,
            playerProperty = MultiLiveFlow.Property((3, SyncPropertyEncoding.Int32(2))) };
        var guest = new RoomSyncPlayer { userId = "22", index = 30,
            playerProperty = MultiLiveFlow.Property((3, SyncPropertyEncoding.Int32(1))) };
        check(MultiLiveFlow.PartyOrder([owner, guest]).SequenceEqual(new long[] { 22, 11 }),
            "本场成员按正的ORDER属性排序，不按旧入房索引或房主身份排序");
        owner.playerProperty = MultiLiveFlow.Property((3, SyncPropertyEncoding.Int32(0)));
        guest.playerProperty = MultiLiveFlow.Property();
        check(MultiLiveFlow.PartyOrder([guest, owner]).SequenceEqual(new long[] { 11, 22 }),
            "ORDER非正或缺失时沿用各端原始索引排序");
        var push = new DiarkisResponse(2, 10030, 255,
            [.. Frame(DumpSerializer.Serialize(owner)), .. Frame(DumpSerializer.Serialize(guest))], 0);
        check(MultiLiveFlow.ReadPlayers(push).Select(p => p.userId).SequenceEqual(new[] { "11", "22" }),
            "10030多帧玩家同步保留对应身份，不能把另一人的状态算作自己准备");
        check(MultiLiveFlow.ReadPlayers(push with { Command = 10020 }).Length == 2, "玩家属性同步同时接收10020和10030");
        Reject(() => MultiLiveFlow.ReadPlayers(push with { Status = 1 }));
        Reject(() => MultiLiveFlow.ReadPlayers(push with { Command = 10010 }));
        Reject(() => MultiLiveFlow.ReadPlayers(push with { Payload = [.. push.Payload, 0] }));
        var room = MultiLiveFlow.Property((2, SyncPropertyEncoding.Int32((int)RoomStepType.Loading)));
        var roomPush = new DiarkisResponse(2, 10010, 255, Frame(DumpSerializer.Serialize(room)), 0);
        check(SyncPropertyEncoding.ReadInt32(MultiLiveFlow.ReadRoom(roomPush).Single().values[2]) == 5,
            "房间Loading为STEP5，与玩家Loading7分开");
        Reject(() => MultiLiveFlow.ReadRoom(roomPush with { Status = 1 }));
        Reject(() => MultiLiveFlow.ReadRoom(roomPush with { Version = 1 }));

        var user = new SuiteUser { userGamedata = new() { userId = 11, deck = 4 } };
        var song = new MultiLiveSong(37, 372, 371, "normal");
        var request = MultiLiveFlow.PrepareRequest(user, "fixture-room", 7, song, [22, 11]);
        check(request.userId == 11 && request.deckId == 4 && request.boostCount == 0 && request.privateFlg
            && request.multiLiveLobbyId == 7 && request.photonRoomName == "fixture-room"
            && request.musicId == 37 && request.musicDifficultyId == 372 && request.musicVocalId == 371,
            "房主准备使用当前账号、主编队、master难度和版本，体力消耗为零");
        check(request.userId1 == 22 && request.userId2 == 11 && request.userId3 == 0 && request.userId4 == 0 && request.userId5 == 0
            && request.selectedMusicId1 == 37 && request.selectedMusicId2 == 37 && request.selectedMusicId3 == 0
            && request.selectedMusicId4 == 0 && request.selectedMusicId5 == 0,
            "两人演出保留本场排序，空槽的账号和选曲均为零");
        check(request.privateRoomSettings.liveRuleType == "normal" && request.privateRoomSettings.musicDifficultyTypes == null
            && request.privateRoomSettings.deckTotalPowerRangeUpper == null, "普通私房不冒充自定义规则或虚构总力上下限");
        Reject(() => MultiLiveFlow.PrepareRequest(user, "fixture-room", 7, song, [11, 11]));
        Reject(() => MultiLiveFlow.PrepareRequest(user, "fixture-room", 7, song, [22, 33]));
        var member = MultiLiveFlow.MemberRequest(user, "fixture-room", 7, song);
        check(member.boostCount == 0 && member.musicCategoryName == "image" && member.musicId == request.musicId
            && member.musicDifficultyId == request.musicDifficultyId && member.privateRoomSettings.liveRuleType == "normal",
            "成员登记同一场歌曲，使用image类别和零体力请求");
        var prepareDefinition = Operations.All["multi-live-prepare"];
        var encoded = Operations.EncodeBody(prepareDefinition, JsonSerializer.SerializeToNode(request, DumpJson.Options)!.AsObject())!;
        check(DumpSerializer.Deserialize<MultiLiveRequest>(encoded).userId2 == 11 && prepareDefinition.Method == "POST"
            && Operations.Path(prepareDefinition, new(), 11) == "/api/multi-live" && !prepareDefinition.Snapshot,
            "全场准备按dump请求发往POST multi-live，不套用个人live路径");
        var memberDefinition = Operations.All["multi-live-member-start"];
        check(memberDefinition.Method == "POST" && Operations.Path(memberDefinition,
            new() { Args = new() { ["liveId"] = "fixture-live" } }, 11) == "/api/user/11/multi-live/fixture-live",
            "成员登记路径同时包含当前账号与房主返回的Live ID");
        var redacted = new Redactor().Clean(JsonSerializer.SerializeToNode(request, DumpJson.Options))!.AsObject();
        check(Enumerable.Range(1, 5).All(i => redacted["userId" + i]!.GetValue<string>() == "<redacted>")
            && redacted["photonRoomName"]!.GetValue<string>() == "<redacted>", "采样记录隐藏全部五槽账号及房间名");
        void Reject(Action action)
        {
            var rejected = false;
            try { action(); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "错误或不完整同步不能作为演出准备成功");
        }
        static byte[] Frame(byte[] data)
        {
            var bytes = new byte[4 + data.Length];
            BinaryPrimitives.WriteInt32BigEndian(bytes, data.Length);
            data.CopyTo(bytes, 4);
            return bytes;
        }
    }
}
