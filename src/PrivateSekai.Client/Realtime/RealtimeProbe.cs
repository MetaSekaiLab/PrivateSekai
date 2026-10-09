using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace PrivateSekai.Client.Realtime;

public static class RealtimeProbe
{
    public static async Task<bool> Run(string configurationPath, string targetName)
    {
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
