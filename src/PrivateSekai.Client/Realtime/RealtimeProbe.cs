extern alias game;

using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using game::Sekai.MultiLive;
using PrivateSekai.Protocol;

namespace PrivateSekai.Client.Realtime;

public static class RealtimeProbe
{
    public static async Task<bool> Run(string configurationPath, string targetName, string? clientConfigsPath = null,
        int? lobbyId = null, string? masterDirectory = null, string? guestConfigurationPath = null, MultiLiveSong? song = null,
        MultiLivePerformance? performance = null, bool replay = false, string? observerConfigurationPath = null,
        bool transferOwnership = false)
    {
        if (lobbyId.HasValue && (lobbyId <= 0 || clientConfigsPath == null))
            throw new InvalidOperationException("房间设置需要客户端配置和有效大厅 ID。");
        if (masterDirectory != null && !lobbyId.HasValue)
            throw new InvalidOperationException("创建房间需要指定大厅。");
        if (guestConfigurationPath != null && masterDirectory == null)
            throw new InvalidOperationException("双账号私房需要完整 master 数据。");
        if (song != null && guestConfigurationPath == null)
            throw new InvalidOperationException("演出开始流程需要双账号私房。");
        if (performance != null && song == null)
            throw new InvalidOperationException("演出结算需要歌曲及双账号私房。");
        if (replay && performance == null) throw new InvalidOperationException("再演需要完整演出成绩用例。");
        if (observerConfigurationPath != null && !replay) throw new InvalidOperationException("再演后入房验证需要双场演出。");
        if (transferOwnership && (performance == null || replay))
            throw new InvalidOperationException("房主迁移验证需要一场完整演出，不能同时选择再演。");
        var guestSong = song != null && performance?.GuestDifficulty is { } guestDifficulty
            ? MultiLiveSong.Read(masterDirectory!, song.MusicId, guestDifficulty, song.VocalId) : song;
        var roomTtl = clientConfigsPath == null ? (int?)null : MultiLiveReservation.ReadRoomTtl(
            JsonNode.Parse(File.ReadAllText(clientConfigsPath))!.AsArray(), LiveRuleType.normal);
        var configuration = JsonFiles.Read<ClientConfiguration>(configurationPath);
        if (!configuration.Targets.TryGetValue(targetName, out var target))
            throw new InvalidOperationException("目标不存在。");
        foreach (var otherPath in new[] { guestConfigurationPath, observerConfigurationPath }.Where(p => p != null))
        {
            var other = JsonFiles.Read<ClientConfiguration>(otherPath!);
            if (!other.Targets.TryGetValue(targetName, out var guestTarget) || guestTarget.Kind != target.Kind || guestTarget.Validate() != target.Validate())
                throw new InvalidOperationException("双账号私房必须使用同一服务端与目标类型。");
        }
        var authStep = new ScenarioStep { Operation = "auth" };
        var realtimeStep = new ScenarioStep { Operation = "diarkis-auth", Args = new() { ["diarkisServerType"] = "multi" } };
        ScenarioRunner.Validate(new Scenario { Steps = [authStep, realtimeStep] }, [target], new HashSet<string> { "auth" });
        var output = Path.GetFullPath(Path.Combine("tools/captures/diarkis-probe",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(output);
        using var http = new ProtocolClient(target, Path.GetDirectoryName(Path.GetFullPath(configurationPath))!);
        if (target.Kind == "official" && !http.OwnTestAccount)
            throw new InvalidOperationException("实时调试仅用于留档的测试账号。");
        http.CaptureTo(Path.Combine(output, "http"));
        var report = new JsonObject { ["complete"] = false, ["serverType"] = "multi", ["phase"] = "auth" };
        DiarkisUdpClient? realtime = null;
        RoomUserBasicInfo? basic = null;
        string? nickname = null;
        SuiteUser? ownerUser = null;
        string? createdRoomId = null;
        var leaveAttempted = false;
        var readbackAttempted = false;
        var received = new JsonArray();
        report["realtimeResponses"] = received;
        async Task<DiarkisResponse> Receive(CancellationToken token)
        {
            var value = await realtime!.ReceiveAsync(token);
            if (received.Count < 64) received.Add(new JsonObject
            {
                ["version"] = value.Version, ["command"] = value.Command, ["status"] = value.Status,
                ["payloadLength"] = value.Payload.Length
            });
            return value;
        }
        async Task<bool> Readback()
        {
            readbackAttempted = true;
            // 旧实例持有账号文件独占锁；先结束旧会话，再建立独立回读会话。
            http.Dispose();
            using var independent = new ProtocolClient(target, Path.GetDirectoryName(Path.GetFullPath(configurationPath))!);
            independent.CaptureTo(Path.Combine(output, "readback-http"));
            await independent.AcquireSignature();
            await independent.Send(authStep);
            await independent.Suite();
            return true;
        }
        async Task Leave(CancellationToken token)
        {
            leaveAttempted = true;
            await realtime!.SendReliableAsync(1, MultiLiveRoom.LeaveCommand,
                MultiLiveRoom.LeaveRequest(createdRoomId!, http.UserId.ToString(CultureInfo.InvariantCulture)), token);
            DiarkisResponse response;
            do { response = await Receive(token); }
            while (response.Version != 1 || response.Command != MultiLiveRoom.LeaveCommand || response.IsPush);
            report["leaveResponseStatus"] = response.Status;
            if (response.Status != 1) throw new InvalidDataException("退出私人房间未成功。");
            report["leaveSucceeded"] = true;
        }
        try
        {
            await http.AcquireSignature();
            await http.Send(authStep);
            if (roomTtl.HasValue)
            {
                report["phase"] = "suite-before-reservation";
                var suite = await http.Suite();
                report["suiteBeforeSucceeded"] = true;
                if (masterDirectory != null)
                {
                    ownerUser = JsonSerializer.Deserialize<SuiteUser>(suite.ToJsonString(), DumpJson.Options)!;
                    basic = new MultiLivePlayer(masterDirectory).Create(ownerUser, suite["now"]!.GetValue<long>());
                    nickname = suite["userGamedata"]!["name"]!.GetValue<string>();
                    report["basicInfoPrepared"] = true;
                    report["totalPower"] = basic.TotalPowerIncludeBuff;
                }
            }
            report["phase"] = "diarkis-auth";
            var authentication = await http.Send(realtimeStep);
            if (authentication["userId"]?.GetValue<long>() != http.UserId)
                throw new InvalidDataException("实时认证与当前账号不一致。");
            report["httpAuthSucceeded"] = true;
            // 连接凭证仅使用本次响应原对象，报告不保存地址、密钥或原始报文。
            realtime = new DiarkisUdpClient(authentication, true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(
                (long)(performance?.DurationMilliseconds ?? 0) * (replay ? 2 : 1) + (replay ? 240000 : guestConfigurationPath == null ? 60000 : 120000)));
            report["phase"] = "udp-handshake";
            await realtime.ConnectAsync(timeout.Token);
            report["udpHandshakeSucceeded"] = true;
            report["phase"] = "client-key-ack";
            await realtime.FlushAsync(timeout.Token);
            report["clientKeyAcknowledged"] = realtime.ClientKeyAcknowledged;
            report["phase"] = "ping";
            var timestamp = new byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(timestamp, (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds);
            await realtime.SendUnreliableAsync(0, 3, timestamp, timeout.Token);
            DiarkisResponse response;
            do { response = await Receive(timeout.Token); }
            while (response.Version != 0 || response.Command != 3 || response.IsPush);
            report["pingResponseStatus"] = response.Status;
            report["pingPayloadLength"] = response.Payload.Length;
            var matches = response.Payload.Length >= 9 && response.Payload.AsSpan(1, 8).SequenceEqual(timestamp);
            report["pingTimestampMatches"] = matches;
            report["phase"] = "echo-keepalive";
            await realtime.WaitForEchoAsync(3, timeout.Token);
            report["echoResponses"] = realtime.EchoResponses;
            report["matchedEchoResponses"] = realtime.MatchedEchoResponses;
            report["echoOffline"] = realtime.LastEcho!.IsOffline;
            report["echoAddressPresent"] = realtime.LastEcho.Address.Length > 0;
            if (roomTtl.HasValue)
            {
                if (!matches || realtime.LastEcho.IsOffline)
                    throw new InvalidOperationException("实时连接验证未通过，停止预留房间。");
                report["phase"] = "reserve-room";
                report["roomTtl"] = roomTtl.Value;
                report["reservationAttempted"] = true;
                // 新建普通预留房间使用零初始化的自定义设置及默认显示房号设置。
                var request = MultiLiveReservation.CreateRequest(LiveRuleType.normal, roomTtl.Value, default, true, null, null);
                await realtime.SendReliableAsync(MultiLiveReservation.Version, MultiLiveReservation.Command, request, timeout.Token);
                do { response = await Receive(timeout.Token); }
                while (response.Version != MultiLiveReservation.Version || response.Command != MultiLiveReservation.Command || response.IsPush);
                report["reservationResponseStatus"] = response.Status;
                report["reservationPayloadLength"] = response.Payload.Length;
                var reserved = MultiLiveReservation.ParseResponse(response);
                report["reservedRoomIdPresent"] = !string.IsNullOrEmpty(reserved.roomId);
                report["roomCreateTime"] = reserved.roomCreateTime;
                report["reservationSucceeded"] = true;
                if (lobbyId.HasValue)
                {
                    report["phase"] = "room-number";
                    var numbered = await realtime.MaintainConnectionAsync(http.Send(new ScenarioStep
                    {
                        Operation = "private-room-number", Args = new() { ["roomId"] = reserved.roomId },
                        Body = new() { ["privateRoomType"] = "multi_live", ["liveRuleType"] = "normal" }
                    }), timeout.Token);
                    var roomNumber = MultiLiveReservation.ReadRoomNumber(numbered, reserved.roomId);
                    report["roomNumberSucceeded"] = true;
                    report["phase"] = "room-settings";
                    var updated = await realtime.MaintainConnectionAsync(http.Send(new ScenarioStep
                    {
                        Operation = "private-room-update", Args = new() { ["roomId"] = reserved.roomId },
                        Body = new() { ["multiLiveLobbyId"] = lobbyId.Value, ["liveRuleType"] = "normal" }
                    }), timeout.Token);
                    if (MultiLiveReservation.ReadRoomNumber(updated, reserved.roomId) != roomNumber
                        || updated["multiLiveLobbyId"]?.GetValue<int>() != lobbyId.Value)
                        throw new InvalidDataException("房间设置响应与已分配房号或请求大厅不一致。");
                    report["roomSettingsSucceeded"] = true;
                    report["multiLiveLobbyId"] = lobbyId.Value;
                    if (basic != null)
                    {
                        report["phase"] = "create-private-room";
                        var createRequest = MultiLiveRoom.CreateRequest(reserved.roomId, roomNumber, nickname!, basic);
                        createdRoomId = reserved.roomId;
                        report["createAttempted"] = true;
                        await realtime.SendReliableAsync(MultiLiveReservation.Version, MultiLiveRoom.CreateCommand,
                            createRequest, timeout.Token);
                        MultiLiveRoomSyncData room = default;
                        while (true)
                        {
                            response = await Receive(timeout.Token);
                            if (response.Version != MultiLiveReservation.Version) continue;
                            if (response.Command == MultiLiveRoom.CreateCommand && !response.IsPush)
                            {
                                report["createResponseStatus"] = response.Status;
                                if (response.Status != 1) throw new InvalidDataException("创建私人房间请求被拒绝。");
                            }
                            if (response.Command is not (MultiLiveRoom.CreatedCommand or MultiLiveRoom.JoinedCommand) || response.IsPush) continue;
                            report["joinResponseCommand"] = response.Command;
                            room = MultiLiveRoom.ParseCreated(response, createdRoomId, roomNumber,
                                http.UserId.ToString(CultureInfo.InvariantCulture), nickname!, basic);
                            report["roomJoined"] = true;
                            report["ownIndex"] = room.index;
                            report["playerCount"] = new[] { room.userId }.Concat((room.players ?? []).Select(p => p.userId)).Distinct().Count();
                            report["basicInfoMatches"] = true;
                            JsonFiles.Write(Path.Combine(output, "created-room.local.json"), JsonSerializer.SerializeToNode(room, DumpJson.Options)!);
                            break;
                        }
                        report["phase"] = "sync-room-number";
                        await realtime.SendReliableAsync(MultiLiveReservation.Version, MultiLiveRoom.PropertyCommand,
                            MultiLiveRoom.RoomNumberRequest(roomNumber), timeout.Token);
                        while (true)
                        {
                            response = await Receive(timeout.Token);
                            if (response.Version == MultiLiveReservation.Version && response.Command == MultiLiveRoom.PropertyCommand && !response.IsPush)
                            {
                                report["roomNumberSyncStatus"] = response.Status;
                                if (response.Status != 1) throw new InvalidDataException("同步私人房号被拒绝。");
                            }
                            if (MultiLiveRoom.ConfirmsRoomNumber(response, roomNumber)) break;
                        }
                        report["roomNumberSyncConfirmed"] = true;
                        if (guestConfigurationPath != null)
                        {
                            report["phase"] = "guest-room-flow";
                            var guestReport = new JsonObject();
                            report["guest"] = guestReport;
                            await MultiLiveGuest.Run(guestConfigurationPath, targetName, masterDirectory!, Path.Combine(output, "guest"),
                                realtime, room, lobbyId.Value, guestReport, timeout.Token, http, ownerUser, song, performance, guestSong,
                                replay, transferOwnership ? configurationPath : null);
                            if (observerConfigurationPath != null)
                            {
                                report["phase"] = "reopen-for-new-member";
                                await realtime.SendReliableAsync(2, MultiLiveRoom.RecreateCommand, [], timeout.Token);
                                await realtime.SendReliableAsync(2, MultiLiveFlow.PlayerPropertyCommand,
                                    DumpSerializer.Serialize(MultiLiveFlow.Status(PlayerStatus.Matching)), timeout.Token);
                                await realtime.FlushAsync(timeout.Token);
                                var observerReport = new JsonObject();
                                report["observer"] = observerReport;
                                await MultiLiveGuest.Run(observerConfigurationPath, targetName, masterDirectory!, Path.Combine(output, "observer"),
                                    realtime, room, lobbyId.Value, observerReport, timeout.Token);
                            }
                        }
                        report["phase"] = "leave-private-room";
                        if (transferOwnership)
                        {
                            leaveAttempted = true;
                            report["leaveSucceeded"] = report["guest"]?["transfer"]?["ownerLeaveSucceeded"]?.GetValue<bool>() == true;
                        }
                        else await Leave(timeout.Token);
                    }
                }
            }
            report["phase"] = "suite-readback";
            if (transferOwnership) await Readback();
            else if (masterDirectory != null) await realtime.MaintainConnectionAsync(Readback(), timeout.Token);
            else await realtime.MaintainConnectionAsync(http.Suite(), timeout.Token);
            report["suiteReadbackSucceeded"] = true;
            report["complete"] = realtime.ClientKeyAcknowledged && matches && !realtime.LastEcho.IsOffline
                && (masterDirectory == null || (report["roomJoined"]?.GetValue<bool>() == true
                    && report["roomNumberSyncConfirmed"]?.GetValue<bool>() == true && report["leaveSucceeded"]?.GetValue<bool>() == true))
                && (guestConfigurationPath == null || report["guest"]?["complete"]?.GetValue<bool>() == true);
            report["phase"] = "finished";
            return report["complete"]!.GetValue<bool>();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            report["errorType"] = ex.GetType().Name;
            if (createdRoomId != null && !leaveAttempted && realtime != null
                && report["guest"]?["transfer"]?["ownerLeaveAttempted"]?.GetValue<bool>() != true)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await Leave(cleanup.Token);
                }
                catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException)
                {
                    report["leaveErrorType"] = cleanupError.GetType().Name;
                }
            }
            if (!readbackAttempted && report["reservationAttempted"]?.GetValue<bool>() == true && report["suiteReadbackSucceeded"] == null)
            {
                // 写请求失败后独立回读，不重发预留，也不让回读错误覆盖原始失败。
                try
                {
                    if (masterDirectory != null) await Readback();
                    else await http.Suite();
                    report["suiteReadbackSucceeded"] = true;
                }
                catch (Exception readbackError) when (readbackError is not OutOfMemoryException)
                {
                    report["suiteReadbackErrorType"] = readbackError.GetType().Name;
                    report["suiteReadbackTransport"] = FailureDiagnostics.Transport(readbackError);
                }
            }
            throw;
        }
        finally
        {
            if (realtime != null)
            {
                report["sentDatagrams"] = realtime.SentDatagrams;
                report["receivedDatagrams"] = realtime.ReceivedDatagrams;
                report["retransmissions"] = realtime.Retransmissions;
                report["redirects"] = realtime.Redirects;
                realtime.Dispose();
            }
            JsonFiles.Write(Path.Combine(output, "report.json"), report);
            Console.WriteLine($"实时调试报告：{Path.GetRelativePath(Directory.GetCurrentDirectory(), output)}");
        }
    }
}
