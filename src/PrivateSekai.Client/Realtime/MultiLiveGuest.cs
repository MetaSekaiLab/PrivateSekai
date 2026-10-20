extern alias game;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using game::CP.Realtime;
using game::Sekai;
using game::Sekai.MultiLive;
using MessagePack;
using PrivateSekai.Protocol;

namespace PrivateSekai.Client.Realtime;

public static class MultiLiveGuest
{
    public static async Task<bool> Run(string configurationPath, string targetName, string masterDirectory, string output,
        DiarkisUdpClient owner, MultiLiveRoomSyncData room, int lobbyId, JsonObject report, CancellationToken cancellationToken,
        ProtocolClient? ownerHttp = null, SuiteUser? ownerUser = null, MultiLiveSong? song = null, MultiLivePerformance? performance = null,
        MultiLiveSong? guestSong = null, bool replay = false, string? ownerConfigurationPath = null)
    {
        if (song != null && (ownerHttp == null || ownerUser == null))
            throw new InvalidOperationException("开始演出需要房主的当前HTTP会话及用户快照。");
        if (replay && (performance == null || song == null))
            throw new InvalidOperationException("再演需要完整成绩用例。");
        if (ownerConfigurationPath != null && (performance == null || song == null || replay))
            throw new InvalidOperationException("房主迁移需要完整演出用例，不能同时选择再演。");
        var target = JsonFiles.Read<ClientConfiguration>(configurationPath).Targets[targetName];
        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configurationPath))!;
        using var http = new ProtocolClient(target, configDirectory);
        if (http.UserId.ToString(CultureInfo.InvariantCulture) == room.userId
            || (target.Kind == "official" && !http.OwnTestAccount))
            throw new InvalidOperationException("加入方必须为另一个留档测试账号。");
        http.CaptureTo(Path.Combine(output, "http"));
        var auth = new ScenarioStep { Operation = "auth" };
        DiarkisUdpClient? guest = null;
        RoomUserBasicInfo? basic = null;
        string? nickname = null;
        SuiteUser? guestUser = null;
        RoomSyncPlayer guestSeen = default;
        var guestId = http.UserId.ToString(CultureInfo.InvariantCulture);
        var number = checked((int)room.privateRoomNumber);
        var joinAttempted = false;
        var leaveAttempted = false;
        var readbackAttempted = false;
        report["complete"] = false;
        var received = new JsonArray();
        report["realtimeResponses"] = received;
        void RecordPlayer(string field, DynamicPropertyPayload actual, DynamicPropertyPayload expected)
        {
            var actualInfo = JsonNode.Parse(MessagePackSerializer.ConvertToJson(
                DumpSerializer.Serialize(SyncPropertyEncoding.ReadObject<RoomUserBasicInfo>(actual.values[2]))))!.AsArray();
            var expectedInfo = JsonNode.Parse(MessagePackSerializer.ConvertToJson(
                DumpSerializer.Serialize(SyncPropertyEncoding.ReadObject<RoomUserBasicInfo>(expected.values[2]))))!.AsArray();
            report[field] = new JsonObject
            {
                ["nicknameMatches"] = SyncPropertyEncoding.ReadString(actual.values[1]) == SyncPropertyEncoding.ReadString(expected.values[1]),
                ["routeMatches"] = SyncPropertyEncoding.ReadInt32(actual.values[5]) == SyncPropertyEncoding.ReadInt32(expected.values[5]),
                ["difficultyMatches"] = SyncPropertyEncoding.ReadString(actual.values[7]) == SyncPropertyEncoding.ReadString(expected.values[7]),
                ["basicDifferentFields"] = new JsonArray(Enumerable.Range(0, expectedInfo.Count)
                    .Where(i => !JsonNode.DeepEquals(actualInfo[i], expectedInfo[i])).Select(i => (JsonNode?)JsonValue.Create(i)).ToArray())
            };
        }
        async Task<DiarkisResponse> Receive(CancellationToken token)
        {
            var response = await guest!.ReceiveAsync(token);
            if (received.Count < 64) received.Add(new JsonObject
            {
                ["version"] = response.Version, ["command"] = response.Command,
                ["status"] = response.Status, ["payloadLength"] = response.Payload.Length
            });
            return response;
        }
        async Task<bool> Prepare()
        {
            report["phase"] = "guest-auth-and-suite";
            await http.AcquireSignature();
            await http.Send(auth);
            var suite = await http.Suite();
            guestUser = JsonSerializer.Deserialize<SuiteUser>(suite.ToJsonString(), DumpJson.Options)!;
            basic = new MultiLivePlayer(masterDirectory).Create(guestUser, suite["now"]!.GetValue<long>());
            nickname = suite["userGamedata"]!["name"]!.GetValue<string>();
            report["basicInfoPrepared"] = true;
            report["phase"] = "lookup-room-number";
            var lookup = await http.Send(new ScenarioStep
            {
                Operation = "private-room-lookup",
                Args = new() { ["privateRoomType"] = "multi_live", ["roomNo"] = number.ToString(CultureInfo.InvariantCulture) }
            });
            if (MultiLiveReservation.ReadRoomNumber(lookup, room.roomId) != number || lookup["multiLiveLobbyId"]?.GetValue<int>() != lobbyId)
                throw new InvalidDataException("查询结果与房主的私人房间、房号或大厅不一致。");
            report["roomLookupMatches"] = true;
            report["phase"] = "guest-udp-connect";
            var authentication = await http.Send(new ScenarioStep { Operation = "diarkis-auth", Args = new() { ["diarkisServerType"] = "multi" } });
            if (authentication["userId"]?.GetValue<long>() != http.UserId) throw new InvalidDataException("加入方实时认证账号不一致。");
            guest = new DiarkisUdpClient(authentication, true);
            await guest.ConnectAsync(cancellationToken);
            await guest.FlushAsync(cancellationToken);
            await guest.WaitForEchoAsync(1, cancellationToken);
            if (guest.LastEcho!.IsOffline) throw new InvalidDataException("加入方实时连接已标记离线。");
            report["udpConnected"] = guest.ClientKeyAcknowledged;
            return true;
        }
        async Task<MultiLiveRoomSyncData> Join()
        {
            report["phase"] = "join-private-room";
            var request = MultiLiveRoom.JoinRequest(room.roomId, nickname!, basic!);
            joinAttempted = true;
            await guest!.SendReliableAsync(MultiLiveReservation.Version, MultiLiveRoom.JoinCommand, request, cancellationToken);
            while (true)
            {
                var response = await Receive(cancellationToken);
                if (response.Version != MultiLiveReservation.Version || response.IsPush) continue;
                if (response.Command == MultiLiveRoom.JoinCommand && response.Status != 1)
                    throw new InvalidDataException("加入私人房间被拒绝。");
                if (response.Command is not (MultiLiveRoom.CreatedCommand or MultiLiveRoom.JoinedCommand)) continue;
                var snapshot = DumpSerializer.Deserialize<MultiLiveRoomSyncData>(response.Payload);
                JsonFiles.Write(Path.Combine(output, "join-state.local.json"), JsonSerializer.SerializeToNode(snapshot, DumpJson.Options)!);
                report["joinChecks"] = new JsonObject
                {
                    ["roomMatches"] = snapshot.roomId == room.roomId, ["roomNumberMatches"] = snapshot.privateRoomNumber == number,
                    ["roomNumberZero"] = snapshot.privateRoomNumber == 0, ["selfMatches"] = snapshot.userId == guestId,
                    ["ownerMatches"] = snapshot.ownerId == room.ownerId, ["published"] = snapshot.releaseRoom,
                    ["membersIncludeOwner"] = snapshot.players?.Any(p => p.userId == room.userId) == true,
                    ["membersIncludeSelf"] = snapshot.players?.Any(p => p.userId == guestId) == true
                };
                RecordPlayer("ownPlayerChecks", snapshot.playerProperty, MultiLiveRoom.PlayerProperty(nickname!, basic!));
                return MultiLiveRoom.ParseCreated(response, room.roomId, number, guestId, nickname!, basic!, room.ownerId);
            }
        }
        async Task<bool> SyncRoomNumber()
        {
            await guest!.SendReliableAsync(MultiLiveReservation.Version, MultiLiveRoom.PropertyCommand,
                MultiLiveRoom.RoomNumberRequest(number), cancellationToken);
            while (true)
            {
                var response = await Receive(cancellationToken);
                if (response.Version == MultiLiveReservation.Version && response.Command == MultiLiveRoom.PropertyCommand
                    && !response.IsPush && response.Status != 1) throw new InvalidDataException("加入方房号同步被拒绝。");
                if (MultiLiveRoom.ConfirmsRoomNumber(response, number)) break;
            }
            return true;
        }
        async Task<bool> OwnerSeesJoin()
        {
            var members = new HashSet<string> { room.userId };
            while (true)
            {
                var response = await owner.ReceiveAsync(cancellationToken);
                if (response.Version != MultiLiveReservation.Version || response.Command != MultiLiveRoom.JoinedCommand || !response.IsPush) continue;
                var seen = false;
                var foundIndex = 0;
                foreach (var player in MultiLiveRoom.ReadMemberJoins(response))
                {
                    members.Add(player.userId);
                    if (player.userId != guestId) continue;
                    MultiLiveRoom.RequirePlayer(player.playerProperty, MultiLiveRoom.PlayerProperty(nickname!, basic!));
                    foundIndex = player.index;
                    guestSeen = player;
                    seen = true;
                }
                if (!seen) continue;
                report["guestIndexFromPush"] = foundIndex;
                if (foundIndex == 0) report["guestIndexResolved"] = await ReadIndex(owner, guestId);
                if (members.Count != 2) throw new InvalidDataException("房主看到的成员人数不一致。");
                report["ownerPlayerCount"] = members.Count;
                return true;
            }
        }
        async Task<int> ReadIndex(DiarkisUdpClient connection, string userId)
        {
            await connection.SendReliableAsync(MultiLiveReservation.Version, MultiLiveRoom.UserIndexCommand,
                MultiLiveRoom.UserIndexRequest(userId), cancellationToken);
            DiarkisResponse response;
            do { response = connection == guest ? await Receive(cancellationToken) : await connection.ReceiveAsync(cancellationToken); }
            while (response.Version != MultiLiveReservation.Version || response.Command != MultiLiveRoom.UserIndexCommand || response.IsPush);
            return MultiLiveRoom.ParseUserIndex(response, userId);
        }
        async Task<bool> Leave(CancellationToken token)
        {
            leaveAttempted = true;
            await guest!.SendReliableAsync(1, MultiLiveRoom.LeaveCommand, MultiLiveRoom.LeaveRequest(room.roomId, guestId), token);
            DiarkisResponse response;
            do { response = await Receive(token); }
            while (response.Version != 1 || response.Command != MultiLiveRoom.LeaveCommand || response.IsPush);
            report["leaveResponseStatus"] = response.Status;
            if (response.Status != 1) throw new InvalidDataException("加入方退出房间未成功。");
            report["leaveSucceeded"] = true;
            return true;
        }
        async Task<bool> OwnerSeesLeave()
        {
            while (true)
            {
                var response = await owner.ReceiveAsync(cancellationToken);
                if (response.Version == 1 && response.Command == MultiLiveRoom.LeaveCommand && response.IsPush
                    && MultiLiveRoom.ReadMemberLeaves(response, room.roomId).Contains(guestId)) return true;
            }
        }
        async Task<bool> Readback()
        {
            readbackAttempted = true;
            http.Dispose();
            using var independent = new ProtocolClient(target, configDirectory);
            independent.CaptureTo(Path.Combine(output, "readback-http"));
            await independent.AcquireSignature();
            await independent.Send(auth);
            await independent.Suite();
            report["suiteReadbackSucceeded"] = true;
            return true;
        }
        async Task Transfer(MultiLiveRoomSyncData joined)
        {
            var transfer = new JsonObject();
            report["transfer"] = transfer;
            transfer["ownerLeaveAttempted"] = true;
            report["phase"] = "owner-leave-and-transfer";
            await owner.SendReliableAsync(1, MultiLiveRoom.LeaveCommand,
                MultiLiveRoom.LeaveRequest(room.roomId, room.userId), cancellationToken);
            async Task<bool> ConfirmOwnerLeave()
            {
                while (true)
                {
                    var response = await owner.ReceiveAsync(cancellationToken);
                    if (response.Version != 1 || response.Command != MultiLiveRoom.LeaveCommand || response.IsPush) continue;
                    if (response.Status != 1) throw new InvalidDataException("原房主退出未成功。");
                    transfer["ownerLeaveSucceeded"] = true;
                    return true;
                }
            }
            await guest!.MaintainConnectionAsync(ConfirmOwnerLeave(), cancellationToken);
            var sawLeave = false;
            var sawTransfer = false;
            while (!sawLeave || !sawTransfer)
            {
                var response = await Receive(cancellationToken);
                if (response.Version == 1 && response.Command == MultiLiveRoom.LeaveCommand && response.IsPush)
                    sawLeave |= MultiLiveRoom.ReadMemberLeaves(response, room.roomId).Contains(room.userId);
                if (response.Command != MultiLiveRoom.MasterChangedCommand || !response.IsPush) continue;
                var owners = MultiLiveRoom.ReadMasterChanges(response);
                JsonFiles.Write(Path.Combine(output, "owner-transfer.local.json"), new JsonObject
                {
                    ["version"] = response.Version, ["oldOwner"] = room.userId,
                    ["owners"] = JsonSerializer.SerializeToNode(owners), ["payload"] = Convert.ToBase64String(response.Payload)
                });
                if (owners.LastOrDefault() != guestId) throw new InvalidDataException("新房主与剩余成员不一致。");
                sawTransfer = true;
                transfer["notificationVersion"] = response.Version;
            }
            transfer["memberLeaveConfirmed"] = sawLeave;
            transfer["masterChangeConfirmed"] = sawTransfer;
            joined.ownerId = guestId;
            joined.players = joined.players.Where(player => player.userId != room.userId).ToArray();
            ownerHttp!.Dispose();
            report["phase"] = "original-owner-rejoin";
            var rejoin = new JsonObject();
            transfer["rejoin"] = rejoin;
            await Run(ownerConfigurationPath!, targetName, masterDirectory, Path.Combine(output, "transfer"),
                guest, joined, lobbyId, rejoin, cancellationToken, http, guestUser, song, performance, guestSong);
            transfer["complete"] = true;
        }
        try
        {
            await owner.MaintainConnectionAsync(Prepare(), cancellationToken);
            var joined = await owner.MaintainConnectionAsync(Join(), cancellationToken);
            report["phase"] = "validate-guest-members";
            var members = joined.players ?? [];
            var ownerMember = members.Single(p => p.userId == room.userId);
            RecordPlayer("ownerPlayerChecks", ownerMember.playerProperty, room.playerProperty);
            MultiLiveRoom.RequirePlayer(ownerMember.playerProperty, room.playerProperty);
            report["ownerIndexMissing"] = ownerMember.index == 0;
            report["ownerIndexFromSnapshot"] = ownerMember.index;
            report["ownerIndexAtCreation"] = room.index;
            if (ownerMember.index == 0) report["ownerIndexResolved"] = await owner.MaintainConnectionAsync(ReadIndex(guest!, room.userId), cancellationToken);
            if (!members.Select(p => p.userId).Append(joined.userId).ToHashSet()
                    .SetEquals(new[] { room.userId, guestId })) throw new InvalidDataException("加入方看到的成员与私人房间不一致。");
            report["roomJoined"] = true;
            report["guestPlayerCount"] = 2;
            report["ownerInfoMatches"] = true;
            report["phase"] = "sync-room-number";
            await owner.MaintainConnectionAsync(SyncRoomNumber(), cancellationToken);
            report["roomNumberSyncConfirmed"] = true;
            report["phase"] = "owner-member-join";
            await guest!.MaintainConnectionAsync(OwnerSeesJoin(), cancellationToken);
            report["ownerSeesGuest"] = true;
            if (ownerConfigurationPath != null) await Transfer(joined);
            else if (song != null)
            {
                var flow = new JsonObject();
                report["live"] = flow;
                await MultiLiveFlow.Run(owner, guest, ownerHttp!, http, ownerUser!, guestUser!, room, joined,
                    guestSeen, lobbyId, song, output, flow, cancellationToken, performance, guestSong);
                if (replay)
                {
                    var recreate = new JsonObject();
                    report["recreate"] = recreate;
                    await MultiLiveFlow.Recreate(owner, guest, room, joined, output, recreate, cancellationToken);
                    var lookup = await owner.MaintainConnectionAsync(guest.MaintainConnectionAsync(http.Send(new ScenarioStep
                    {
                        Operation = "private-room-lookup", Args = new() { ["privateRoomType"] = "multi_live", ["roomNo"] = number.ToString(CultureInfo.InvariantCulture) }
                    }), cancellationToken), cancellationToken);
                    if (MultiLiveReservation.ReadRoomNumber(lookup, room.roomId) != number)
                        throw new InvalidDataException("再演后的房间或房号不一致。");
                    recreate["roomLookupMatches"] = true;
                    ownerUser = JsonSerializer.Deserialize<SuiteUser>((await owner.MaintainConnectionAsync(
                        guest.MaintainConnectionAsync(ownerHttp!.Suite(), cancellationToken), cancellationToken)).ToJsonString(), DumpJson.Options)!;
                    guestUser = JsonSerializer.Deserialize<SuiteUser>((await owner.MaintainConnectionAsync(
                        guest.MaintainConnectionAsync(http.Suite(), cancellationToken), cancellationToken)).ToJsonString(), DumpJson.Options)!;
                    var second = new JsonObject();
                    report["replayLive"] = second;
                    var replayOutput = Path.Combine(output, "replay");
                    Directory.CreateDirectory(replayOutput);
                    await MultiLiveFlow.Run(owner, guest, ownerHttp!, http, ownerUser, guestUser, room, joined,
                        guestSeen, lobbyId, song, replayOutput, second, cancellationToken, performance, guestSong);
                    recreate["complete"] = true;
                }
            }
            report["phase"] = "guest-leave";
            if (ownerConfigurationPath == null) await owner.MaintainConnectionAsync(Leave(cancellationToken), cancellationToken);
            else await Leave(cancellationToken);
            if (ownerConfigurationPath == null)
            {
                await guest.MaintainConnectionAsync(OwnerSeesLeave(), cancellationToken);
                report["ownerSeesGuestLeave"] = true;
            }
            report["phase"] = "guest-independent-readback";
            if (ownerConfigurationPath == null) await owner.MaintainConnectionAsync(Readback(), cancellationToken);
            else await Readback();
            report["complete"] = true;
            report["phase"] = "finished";
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            report["errorType"] = error.GetType().Name;
            if (joinAttempted && !leaveAttempted && guest != null)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    if (ownerConfigurationPath == null) await owner.MaintainConnectionAsync(Leave(cleanup.Token), cleanup.Token);
                    else await Leave(cleanup.Token);
                }
                catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException)
                {
                    report["leaveErrorType"] = cleanupError.GetType().Name;
                }
            }
            if (joinAttempted && !readbackAttempted)
            {
                try { await Readback(); }
                catch (Exception readbackError) when (readbackError is not OutOfMemoryException)
                {
                    report["suiteReadbackErrorType"] = readbackError.GetType().Name;
                }
            }
            throw;
        }
        finally
        {
            if (guest != null)
            {
                report["sentDatagrams"] = guest.SentDatagrams;
                report["receivedDatagrams"] = guest.ReceivedDatagrams;
                report["retransmissions"] = guest.Retransmissions;
                report["redirects"] = guest.Redirects;
                guest.Dispose();
            }
        }
    }
}
