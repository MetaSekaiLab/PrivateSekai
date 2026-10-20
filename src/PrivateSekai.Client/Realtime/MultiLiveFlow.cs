extern alias game;

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using game::CP.Realtime;
using game::Sekai;
using game::Sekai.MultiLive;
using PrivateSekai.Protocol;

namespace PrivateSekai.Client.Realtime;

public sealed record MultiLiveSong(int MusicId, int DifficultyId, int VocalId, string Difficulty)
{
    public static MultiLiveSong Read(string directory, int musicId, string difficulty, int vocalId)
    {
        if (!Enum.TryParse<MusicDifficulty>(difficulty, out var parsed) || parsed == MusicDifficulty.none)
            throw new InvalidDataException("演出难度无效。");
        var difficulties = JsonSerializer.Deserialize<MasterMusicDifficulty[]>(
            File.ReadAllText(Path.Combine(directory, "musicDifficulties.json")), DumpJson.Options)!;
        var vocals = JsonSerializer.Deserialize<MasterMusicVocal[]>(
            File.ReadAllText(Path.Combine(directory, "musicVocals.json")), DumpJson.Options)!;
        var score = difficulties.Single(d => d.musicId == musicId && d.musicDifficulty == difficulty);
        if (!vocals.Any(v => v.id == vocalId && v.musicId == musicId))
            throw new InvalidDataException("演唱版本与歌曲不一致。");
        return new(musicId, score.id, vocalId, difficulty);
    }
}

public static class MultiLiveFlow
{
    public const ushort PlayerPropertyCommand = 10030;

    public static DynamicPropertyPayload Property(params (int Key, byte[] Value)[] values) => new()
    {
        isRSend = 1, values = values.ToDictionary(v => v.Key, v => v.Value)
    };

    public static DynamicPropertyPayload Status(PlayerStatus status) => Property((4, SyncPropertyEncoding.Int32((int)status)));

    public static DynamicPropertyPayload SelectMusic(int musicId) => Property(
        (8, SyncPropertyEncoding.Int32(musicId)), (9, SyncPropertyEncoding.Int32(0)),
        (4, SyncPropertyEncoding.Int32((int)PlayerStatus.FixedMusic)));

    public static DynamicPropertyPayload Ready(string difficulty) => Property(
        (7, SyncPropertyEncoding.String(difficulty)), (11, SyncPropertyEncoding.String("")),
        (4, SyncPropertyEncoding.Int32((int)PlayerStatus.ReadyToLive)));

    public static RoomSyncPlayer[] ReadPlayers(DiarkisResponse response)
    {
        if (response.Version != 2 || response.Command is not (10020 or 10030) || !response.IsPush)
            throw new InvalidDataException("未收到玩家属性推送。");
        return MultiLiveRoom.ReadFrames(response.Payload).Select(DumpSerializer.Deserialize<RoomSyncPlayer>).ToArray();
    }

    public static DynamicPropertyPayload[] ReadRoom(DiarkisResponse response)
    {
        if (response.Version != 2 || response.Command != 10010 || !response.IsPush)
            throw new InvalidDataException("未收到房间属性推送。");
        return MultiLiveRoom.ReadFrames(response.Payload).Select(DumpSerializer.Deserialize<DynamicPropertyPayload>).ToArray();
    }

    public static bool Matches(DynamicPropertyPayload actual, DynamicPropertyPayload expected) =>
        expected.values.All(pair => actual.values?.TryGetValue(pair.Key, out var value) == true
            && pair.Value.AsSpan().SequenceEqual(value));

    public static long[] PartyOrder(IEnumerable<RoomSyncPlayer> players) => players.OrderBy(p =>
        p.playerProperty.values.TryGetValue(3, out var order) && SyncPropertyEncoding.ReadInt32(order) > 0
            ? SyncPropertyEncoding.ReadInt32(order) : p.index)
        .Select(p => long.Parse(p.userId, CultureInfo.InvariantCulture)).ToArray();

    public static MultiLiveRequest PrepareRequest(SuiteUser user, string roomId, int lobbyId, MultiLiveSong song, long[] players)
    {
        if (players.Length is < 2 or > 5 || players.Distinct().Count() != players.Length
            || !players.Contains(user.userGamedata.userId)) throw new InvalidDataException("本场演出成员无效。");
        var ids = players.Concat(new long[5]).Take(5).ToArray();
        return new MultiLiveRequest
        {
            multiLiveLobbyId = lobbyId, photonRoomName = roomId, privateFlg = true,
            musicId = song.MusicId, musicDifficultyId = song.DifficultyId, musicVocalId = song.VocalId,
            userId = user.userGamedata.userId, deckId = user.userGamedata.deck, boostCount = 0,
            userId1 = ids[0], userId2 = ids[1], userId3 = ids[2], userId4 = ids[3], userId5 = ids[4],
            selectedMusicId1 = song.MusicId, selectedMusicId2 = song.MusicId,
            selectedMusicId3 = ids[2] == 0 ? 0 : song.MusicId,
            selectedMusicId4 = ids[3] == 0 ? 0 : song.MusicId,
            selectedMusicId5 = ids[4] == 0 ? 0 : song.MusicId,
            privateRoomSettings = new() { liveRuleType = "normal" }
        };
    }

    public static UserMultiLiveRequest MemberRequest(SuiteUser user, string roomId, int lobbyId, MultiLiveSong song) => new()
    {
        photonRoomName = roomId, privateFlg = true, multiLiveLobbyId = lobbyId, musicId = song.MusicId,
        deckId = user.userGamedata.deck, musicDifficultyId = song.DifficultyId, musicVocalId = song.VocalId,
        boostCount = 0, musicCategoryName = MusicCategory.image.ToString(), privateRoomSettings = new() { liveRuleType = "normal" }
    };

    public static async Task<bool> Run(DiarkisUdpClient owner, DiarkisUdpClient guest, ProtocolClient ownerHttp,
        ProtocolClient guestHttp, SuiteUser ownerUser, SuiteUser guestUser, MultiLiveRoomSyncData room,
        MultiLiveRoomSyncData joined, RoomSyncPlayer guestSeen, int lobbyId, MultiLiveSong song, string output,
        JsonObject report, CancellationToken token, MultiLivePerformance? performance = null)
    {
        var ownerId = room.userId;
        var guestId = joined.userId;
        var ownerScore = performance == null ? null : MultiLivePerformance.Score(performance.OwnerScore, ownerHttp.UserId, song.DifficultyId);
        var guestScore = performance == null ? null : MultiLivePerformance.Score(performance.GuestScore, guestHttp.UserId, song.DifficultyId);
        var ownerPlayers = new Dictionary<string, RoomSyncPlayer>
        {
            [ownerId] = new() { userId = ownerId, index = room.index, playerProperty = room.playerProperty },
            [guestId] = guestSeen
        };
        var guestPlayers = joined.players.Append(new RoomSyncPlayer
            { userId = guestId, index = joined.index, playerProperty = joined.playerProperty }).DistinctBy(p => p.userId).ToDictionary(p => p.userId);
        var ownerRoom = room.roomProperty;
        var guestRoom = joined.roomProperty;
        var stages = new JsonArray();
        report["stages"] = stages;
        report["complete"] = false;
        void Merge(DynamicPropertyPayload current, DynamicPropertyPayload update)
        {
            current.values ??= [];
            foreach (var pair in update.values) current.values[pair.Key] = pair.Value;
        }
        async Task<bool> Wait(DiarkisUdpClient connection, string? playerId, DynamicPropertyPayload expected)
        {
            var players = connection == owner ? ownerPlayers : guestPlayers;
            var properties = connection == owner ? ownerRoom : guestRoom;
            while (true)
            {
                var response = await connection.ReceiveAsync(token);
                if (response.Version == 1 && response.Command == MultiLiveRoom.LeaveCommand && response.IsPush
                    && MultiLiveRoom.ReadMemberLeaves(response, room.roomId).Length > 0)
                    throw new InvalidDataException("演出同步期间有成员离开房间。");
                if (response.Version == 2 && !response.IsPush && response.Status != 1)
                    throw new InvalidDataException("演出流程的实时请求被拒绝。");
                if (response.Version != 2 || !response.IsPush) continue;
                if (response.Command == 10010)
                    foreach (var update in ReadRoom(response)) Merge(properties, update);
                if (response.Command is 10020 or 10030)
                    foreach (var update in ReadPlayers(response))
                    {
                        if (!players.TryGetValue(update.userId, out var current)) throw new InvalidDataException("演出中出现未知成员。");
                        Merge(current.playerProperty, update.playerProperty);
                        players[update.userId] = current;
                    }
                if (playerId == null ? Matches(properties, expected) : Matches(players[playerId].playerProperty, expected)) return true;
            }
        }
        async Task Both(DynamicPropertyPayload property, string stage, DynamicPropertyPayload? otherProperty = null)
        {
            var other = otherProperty ?? property;
            report["phase"] = stage;
            await owner.SendReliableAsync(2, PlayerPropertyCommand, DumpSerializer.Serialize(property), token);
            await guest.SendReliableAsync(2, PlayerPropertyCommand, DumpSerializer.Serialize(other), token);
            await owner.MaintainConnectionAsync(Wait(guest, ownerId, property), token);
            await guest.MaintainConnectionAsync(Wait(owner, guestId, other), token);
            Merge(ownerPlayers[ownerId].playerProperty, property);
            Merge(guestPlayers[guestId].playerProperty, other);
            stages.Add(stage);
        }
        async Task Room(DynamicPropertyPayload property, string stage)
        {
            report["phase"] = stage;
            await owner.SendReliableAsync(2, 10010, DumpSerializer.Serialize(property), token);
            await owner.MaintainConnectionAsync(Wait(guest, null, property), token);
            await guest.MaintainConnectionAsync(Wait(owner, null, property), token);
            stages.Add(stage);
        }
        async Task<T> Http<T>(Task<T> task) => await owner.MaintainConnectionAsync(guest.MaintainConnectionAsync(task, token), token);
        static JsonObject Body<T>(T request) => JsonSerializer.SerializeToNode(request, DumpJson.Options)!.AsObject();

        await Both(Status(PlayerStatus.Matching), "matching");
        await owner.SendReliableAsync(2, 1013, [1], token);
        var seed = room.roomId + unchecked((int)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).ToString(CultureInfo.InvariantCulture);
        await Room(Property((2, SyncPropertyEncoding.Int32(2)), (1, SyncPropertyEncoding.Int32(1)),
            (12, SyncPropertyEncoding.String("")), (13, SyncPropertyEncoding.String(seed))), "fixed-party");
        await Both(Status(PlayerStatus.SelectMusic), "select-music");
        await Both(SelectMusic(song.MusicId), "music-selected");
        var selectedPlayer = Random.Shared.Next(2) == 0 ? ownerId : guestId;
        await Room(Property((2, SyncPropertyEncoding.Int32(3)), (4, SyncPropertyEncoding.Int32(song.MusicId)),
            (11, SyncPropertyEncoding.String(selectedPlayer)), (1, SyncPropertyEncoding.Int32(2))), "shuffle");
        await Room(Property((2, SyncPropertyEncoding.Int32(4))), "ready-final");
        await Both(Status(PlayerStatus.SelectDifficulty), "select-difficulty");
        await Both(Ready(song.Difficulty), "ready-to-live");
        await Room(Property((2, SyncPropertyEncoding.Int32(5))), "loading");
        await Both(Status(PlayerStatus.Loading), "players-loading");
        await Both(Status(PlayerStatus.LoadComplete), "load-complete");

        report["phase"] = "master-prepare-api";
        var order = PartyOrder(ownerPlayers.Values);
        var guestOrder = PartyOrder(guestPlayers.Values);
        // 本机恢复记录先于业务写入落盘，不进入脱敏报告。
        var journalPath = Path.Combine(output, "live-state.local.json");
        var journal = new JsonObject
        {
            ["phase"] = "master-prepare-attempted", ["ownerUserId"] = ownerHttp.UserId, ["guestUserId"] = guestHttp.UserId,
            ["roomId"] = room.roomId, ["musicId"] = song.MusicId, ["difficultyId"] = song.DifficultyId,
            ["vocalId"] = song.VocalId, ["boostCount"] = 0
        };
        JsonFiles.Write(journalPath, journal);
        var prepare = await Http(ownerHttp.Send(new ScenarioStep
            { Operation = "multi-live-prepare", Body = Body(PrepareRequest(ownerUser, room.roomId, lobbyId, song, order)) }));
        var liveId = prepare["liveId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(liveId)) throw new InvalidDataException("房主演出准备未返回 Live ID。");
        journal["liveId"] = liveId;
        journal["phase"] = "master-prepared";
        JsonFiles.Write(journalPath, journal);
        report["masterPrepared"] = true;
        await Room(Property((12, SyncPropertyEncoding.String(liveId))), "live-id-shared");
        await owner.SendReliableAsync(2, PlayerPropertyCommand, DumpSerializer.Serialize(Status(PlayerStatus.PrepareAPIFinished)), token);
        await owner.MaintainConnectionAsync(Wait(guest, ownerId, Status(PlayerStatus.PrepareAPIFinished)), token);
        await owner.SendReliableAsync(2, PlayerPropertyCommand, DumpSerializer.Serialize(Status(PlayerStatus.InGameLiveReady)), token);
        report["phase"] = "member-start-api";
        journal["phase"] = "member-start-attempted";
        JsonFiles.Write(journalPath, journal);
        await Http(guestHttp.Send(new ScenarioStep
        {
            Operation = "multi-live-member-start", Args = new() { ["liveId"] = liveId },
            Body = Body(MemberRequest(guestUser, room.roomId, lobbyId, song))
        }));
        report["memberPrepared"] = true;
        journal["phase"] = "member-prepared";
        JsonFiles.Write(journalPath, journal);
        await guest.SendReliableAsync(2, PlayerPropertyCommand, DumpSerializer.Serialize(Status(PlayerStatus.InGameLiveReady)), token);
        await owner.MaintainConnectionAsync(Wait(guest, ownerId, Status(PlayerStatus.InGameLiveReady)), token);
        await guest.MaintainConnectionAsync(Wait(owner, guestId, Status(PlayerStatus.InGameLiveReady)), token);
        await Room(Property((2, SyncPropertyEncoding.Int32(6))), "live-room");
        async Task<long> Timestamp(DiarkisUdpClient connection)
        {
            var started = Stopwatch.GetTimestamp();
            await connection.SendReliableAsync(2, 10000, [1], token);
            while (true)
            {
                var response = await connection.ReceiveAsync(token);
                if (response.Version != 2 || response.Command != 10000 || response.IsPush) continue;
                if (response.Status != 1) throw new InvalidDataException("演出时钟同步被拒绝。");
                var time = DumpSerializer.Deserialize<TimestampPayload>(response.Payload).timestamp;
                if (time <= 0) throw new InvalidDataException("演出时钟无效。");
                return time + (long)(Stopwatch.GetElapsedTime(started).TotalMilliseconds * 0.5);
            }
        }
        report["phase"] = "synchronize-live-clock";
        await owner.MaintainConnectionAsync(Timestamp(guest), token);
        var serverTime = await guest.MaintainConnectionAsync(Timestamp(owner), token);
        var measured = Stopwatch.GetTimestamp();
        report["serverClocksSynchronized"] = true;
        await Both(Status(PlayerStatus.Live), "players-live");
        var timestamp = serverTime + (long)Stopwatch.GetElapsedTime(measured).TotalMilliseconds;
        await Room(Property((1, SyncPropertyEncoding.Int32(3)), (7, SyncPropertyEncoding.Int64(timestamp)),
            (2, SyncPropertyEncoding.Int32(6))), "start-live");
        report["phase"] = "started";
        journal["phase"] = "started";
        JsonFiles.Write(journalPath, journal);
        if (performance != null)
        {
            report["phase"] = "performing";
            report["durationMilliseconds"] = performance.DurationMilliseconds;
            journal["phase"] = "performing";
            JsonFiles.Write(journalPath, journal);
            async Task<bool> WaitDuration()
            {
                await Task.Delay(performance.DurationMilliseconds, token);
                return true;
            }
            await Http(WaitDuration());
            await Both(Status(PlayerStatus.LiveFinished), "live-finished");
            await Both(MultiLivePerformance.Ready(ownerScore!), "scores-shared", MultiLivePerformance.Ready(guestScore!));
            var fromOwner = ownerPlayers.Values.ToDictionary(p => long.Parse(p.userId, CultureInfo.InvariantCulture),
                p => SyncPropertyEncoding.ReadObject<UserMultiLiveClearScoreRequest>(p.playerProperty.values[10]));
            var fromGuest = guestPlayers.Values.ToDictionary(p => long.Parse(p.userId, CultureInfo.InvariantCulture),
                p => SyncPropertyEncoding.ReadObject<UserMultiLiveClearScoreRequest>(p.playerProperty.values[10]));
            var ownerResult = MultiLivePerformance.Result(order, fromOwner);
            var guestResult = MultiLivePerformance.Result(guestOrder, fromGuest);
            await Room(Property((1, SyncPropertyEncoding.Int32(4)), (2, SyncPropertyEncoding.Int32(7))), "start-result");
            await Both(Status(PlayerStatus.Result), "players-result");
            async Task<bool> Clear(ProtocolClient http, UserMultiLiveClearRequest request, string side)
            {
                report["phase"] = side + "-clear";
                journal["phase"] = side + "-clear-attempted";
                JsonFiles.Write(journalPath, journal);
                var response = await Http(http.Send(new ScenarioStep
                {
                    Operation = "multi-live-clear", Args = new() { ["liveId"] = liveId }, Body = Body(request)
                }));
                if (response["totalScore"]?.GetValue<int>() != request.totalScore)
                    throw new InvalidDataException("结算总分与实际同步成绩不一致。");
                report[side + "Result"] = new JsonObject
                {
                    ["totalScore"] = response["totalScore"]!.DeepClone(), ["scoreRank"] = response["scoreRank"]?.DeepClone(),
                    ["multiScoreRank"] = response["multiScoreRank"]?.DeepClone(), ["userLivePoint"] = response["userLivePoint"]?.DeepClone()
                };
                report[side + "Cleared"] = true;
                journal["phase"] = side + "-cleared";
                JsonFiles.Write(journalPath, journal);
                return true;
            }
            await Clear(ownerHttp, ownerResult, "owner");
            await Clear(guestHttp, guestResult, "guest");
            report["phase"] = "cleared";
            journal["phase"] = "cleared";
            JsonFiles.Write(journalPath, journal);
        }
        report["complete"] = true;
        return true;
    }
}
