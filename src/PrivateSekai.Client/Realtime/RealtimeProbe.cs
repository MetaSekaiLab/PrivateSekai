extern alias game;

using System.Buffers.Binary;
using System.Text.Json.Nodes;
using game::Sekai;

namespace PrivateSekai.Client.Realtime;

public static class RealtimeProbe
{
    public static async Task<bool> Run(string configurationPath, string targetName, string? clientConfigsPath = null, int? lobbyId = null)
    {
        if (lobbyId.HasValue && (lobbyId <= 0 || clientConfigsPath == null))
            throw new InvalidOperationException("房间设置需要客户端配置和有效大厅 ID。");
        var roomTtl = clientConfigsPath == null ? (int?)null : MultiLiveReservation.ReadRoomTtl(
            JsonNode.Parse(File.ReadAllText(clientConfigsPath))!.AsArray(), LiveRuleType.normal);
        var configuration = JsonFiles.Read<ClientConfiguration>(configurationPath);
        if (!configuration.Targets.TryGetValue(targetName, out var target))
            throw new InvalidOperationException("目标不存在。");
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
        try
        {
            await http.AcquireSignature();
            await http.Send(authStep);
            if (roomTtl.HasValue)
            {
                report["phase"] = "suite-before-reservation";
                await http.Suite();
                report["suiteBeforeSucceeded"] = true;
            }
            report["phase"] = "diarkis-auth";
            var authentication = await http.Send(realtimeStep);
            report["httpAuthSucceeded"] = true;
            // 连接凭证仅使用本次响应原对象，报告不保存地址、密钥或原始报文。
            realtime = new DiarkisUdpClient(authentication, true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
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
            do { response = await realtime.ReceiveAsync(timeout.Token); }
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
                do { response = await realtime.ReceiveAsync(timeout.Token); }
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
                    var numbered = await http.Send(new ScenarioStep
                    {
                        Operation = "private-room-number", Args = new() { ["roomId"] = reserved.roomId },
                        Body = new() { ["privateRoomType"] = "multi_live", ["liveRuleType"] = "normal" }
                    });
                    var roomNumber = MultiLiveReservation.ReadRoomNumber(numbered, reserved.roomId);
                    report["roomNumberSucceeded"] = true;
                    report["phase"] = "room-settings";
                    var updated = await http.Send(new ScenarioStep
                    {
                        Operation = "private-room-update", Args = new() { ["roomId"] = reserved.roomId },
                        Body = new() { ["multiLiveLobbyId"] = lobbyId.Value, ["liveRuleType"] = "normal" }
                    });
                    if (MultiLiveReservation.ReadRoomNumber(updated, reserved.roomId) != roomNumber
                        || updated["multiLiveLobbyId"]?.GetValue<int>() != lobbyId.Value)
                        throw new InvalidDataException("房间设置响应与已分配房号或请求大厅不一致。");
                    report["roomSettingsSucceeded"] = true;
                    report["multiLiveLobbyId"] = lobbyId.Value;
                }
            }
            report["phase"] = "suite-readback";
            await http.Suite();
            report["suiteReadbackSucceeded"] = true;
            report["complete"] = realtime.ClientKeyAcknowledged && matches && !realtime.LastEcho.IsOffline;
            report["phase"] = "finished";
            return report["complete"]!.GetValue<bool>();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            report["errorType"] = ex.GetType().Name;
            if (report["reservationAttempted"]?.GetValue<bool>() == true && report["suiteReadbackSucceeded"] == null)
            {
                // 写请求失败后独立回读，不重发预留，也不让回读错误覆盖原始失败。
                try
                {
                    await http.Suite();
                    report["suiteReadbackSucceeded"] = true;
                }
                catch (Exception readbackError) when (readbackError is not OutOfMemoryException)
                {
                    report["suiteReadbackErrorType"] = readbackError.GetType().Name;
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
                realtime.Dispose();
            }
            JsonFiles.Write(Path.Combine(output, "report.json"), report);
            Console.WriteLine($"实时调试报告：{Path.GetRelativePath(Directory.GetCurrentDirectory(), output)}");
        }
    }
}
