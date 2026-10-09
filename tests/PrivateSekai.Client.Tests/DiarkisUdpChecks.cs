using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using PrivateSekai.Client.Realtime;

internal static class DiarkisUdpChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var cancellation = timeout.Token;
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var macKey = Enumerable.Range(32, 32).Select(i => (byte)i).ToArray();
        var sid = Enumerable.Range(64, 16).Select(i => (byte)i).ToArray();
        var authentication = new JsonObject
        {
            ["udpHost"] = "127.0.0.1", ["udpPort"] = ((IPEndPoint)server.Client.LocalEndPoint!).Port,
            ["clientKey"] = "synthetic-client-key", ["sid"] = Convert.ToHexString(sid),
            ["encryptionKey"] = Convert.ToHexString(key), ["encryptionIv"] = Convert.ToHexString(iv),
            ["encryptionMacKey"] = Convert.ToHexString(macKey)
        };
        var expectedLarge = Enumerable.Range(0, 1500).Select(i => (byte)i).ToArray();
        var peer = RunPeer();
        using var client = new DiarkisUdpClient(authentication, false);
        await client.ConnectAsync(cancellation);
        await client.FlushAsync(cancellation);
        var notification = await client.ReceiveAsync(cancellation);
        var first = await client.ReceiveAsync(cancellation);
        var second = await client.ReceiveAsync(cancellation);
        var third = await client.ReceiveAsync(cancellation);
        await client.SendUnreliableAsync(0, 3, new byte[8], cancellation);
        var ping = await client.ReceiveAsync(cancellation);
        await client.WaitForEchoAsync(2, cancellation);
        await peer;
        check(notification.IsPush && notification.Command == 99 && notification.Payload.SequenceEqual(new byte[] { 77 }),
            "Echo 内附通知进入业务队列，不随保活响应被丢弃");
        check(first.Command == 20 && first.Payload.SequenceEqual(new byte[] { 1 })
            && second.Command == 21 && second.Payload.SequenceEqual(new byte[] { 2 }),
            "真实回环 UDP 按可靠序号交付乱序响应");
        check(client.ClientKeyAcknowledged && client.Retransmissions == 1,
            "丢失首个 clientKey 消息后以相同序号 RST 重传并接收确认");
        check(third.Command == 22 && third.Payload.SequenceEqual(expectedLarge),
            "重复旧序号只确认不重复交付，分片乱序后恢复并解密完整响应");
        check(ping.Version == 0 && ping.Command == 3 && ping.Status == 1 && ping.Payload.Length == 9,
            "普通 UDP 请求和响应可与可靠消息共用连接");
        check(client.EchoResponses == 2 && client.MatchedEchoResponses == 2 && client.LastEcho?.IsOffline == false,
            "首次与周期 Echo 经真实 UDP 返回并匹配时间戳，内部响应不挤占业务队列");
        using var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var stopped = false;
        try { await client.ReceiveAsync(canceled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        check(stopped, "无报文时调用方可取消接收，不留下后台读取");

        async Task RunPeer()
        {
            var syn = await server.ReceiveAsync(cancellation);
            var endpoint = syn.RemoteEndPoint;
            Expect(syn.Buffer.SequenceEqual(new byte[] { 0, 0, 0, 2 }.Concat(sid)), "SYN 格式");
            await Reply(DiarkisPacket.CreateUdp(2, 7, []));
            var ack = await server.ReceiveAsync(cancellation);
            Expect(ack.Buffer.SequenceEqual(new byte[] { 7, 0, 0, 4 }.Concat(sid)), "SYN ACK 携带 SID");
            var original = await server.ReceiveAsync(cancellation);
            Expect(original.Buffer[3] == 3 && DecodeRequest(original.Buffer).SequenceEqual(Encoding.UTF8.GetBytes("synthetic-client-key")),
                "clientKey 使用 UTF8 并整体加密");
            var firstEcho = await server.ReceiveAsync(cancellation);
            var firstEchoPayload = DecodeRequest(firstEcho.Buffer);
            Expect(BinaryPrimitives.ReadUInt16BigEndian(firstEcho.Buffer.AsSpan(12)) == 1, "首次 Echo 命令");
            var offset = 8;
            while (offset < firstEchoPayload.Length)
            {
                var length = BinaryPrimitives.ReadInt32BigEndian(firstEchoPayload.AsSpan(offset));
                var address = Encoding.UTF8.GetString(firstEchoPayload, offset + 4, length);
                Expect(address.EndsWith($":{endpoint.Port}"), "首次 Echo 地址带实际本地端口");
                offset += 4 + length;
            }
            await Reply(DiarkisPacket.CreateUdp(4, 1, []));
            await Reply(DiarkisPacket.CreateUdp(1, 0, Response(0, 1, EchoPayload(firstEchoPayload, true))));
            var retry = await server.ReceiveAsync(cancellation);
            Expect(retry.Buffer[3] == 5 && retry.Buffer[..3].SequenceEqual(original.Buffer[..3])
                && retry.Buffer[4..].SequenceEqual(original.Buffer[4..]), "重传只改变 DAT 标志");
            await Reply(DiarkisPacket.CreateUdp(3, 8, Response(2, 21, [2])));
            await Ack(8, 4);
            await Reply(DiarkisPacket.CreateUdp(4, 0, []));
            await Reply(DiarkisPacket.CreateUdp(3, 7, Response(2, 20, [1])));
            await Ack(7, 4);
            await Reply(DiarkisPacket.CreateUdp(5, 7, Response(2, 20, [1])));
            await Ack(7, 6);
            var parts = DiarkisSplitPacket.Create(12, Response(2, 22, expectedLarge), 1300);
            await Reply(DiarkisPacket.CreateUdp(3, 10, parts[1]));
            await Ack(10, 4);
            await Reply(DiarkisPacket.CreateUdp(3, 9, parts[0]));
            await Ack(9, 4);
            var pingRequest = await server.ReceiveAsync(cancellation);
            Expect(pingRequest.Buffer[3] == 1 && DecodeRequest(pingRequest.Buffer).Length == 8, "Ping 普通 UDP");
            var pingResponse = Response(0, 3, new byte[9]);
            pingResponse[10] = 1;
            await Reply(DiarkisPacket.CreateUdp(1, 0, pingResponse));
            var laterEcho = await server.ReceiveAsync(cancellation);
            var laterEchoPayload = DecodeRequest(laterEcho.Buffer);
            Expect(laterEchoPayload.Length == 8 && BinaryPrimitives.ReadUInt16BigEndian(laterEcho.Buffer.AsSpan(12)) == 1,
                "周期 Echo 只发送时间戳，不重复地址列表");
            await Reply(DiarkisPacket.CreateUdp(4, 2, []));
            await Reply(DiarkisPacket.CreateUdp(1, 0, Response(0, 1, EchoPayload(laterEchoPayload))));

            async Task Reply(byte[] data) => await server.SendAsync(data, endpoint, cancellation);
            async Task Ack(uint sequence, byte flag)
            {
                var result = await server.ReceiveAsync(cancellation);
                Expect(result.Buffer.SequenceEqual(DiarkisPacket.CreateUdp(flag, sequence, sid)), "数据 ACK/EACK 携带 SID");
            }
        }

        static byte[] EchoPayload(byte[] request, bool withNotification = false)
        {
            var address = Encoding.UTF8.GetBytes("127.0.0.1:1");
            var notification = withNotification ? Convert.FromHexString("00060200634D") : Array.Empty<byte>();
            var payload = new byte[11 + address.Length + notification.Length];
            payload[0] = 1;
            request.AsSpan(0, 8).CopyTo(payload.AsSpan(1));
            address.CopyTo(payload, 9);
            notification.CopyTo(payload, 9 + address.Length);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(payload.Length - 2), (ushort)(9 + address.Length));
            return payload;
        }

        byte[] DecodeRequest(byte[] datagram)
        {
            var packet = datagram.AsSpan(4);
            Expect(packet[..4].SequenceEqual(Convert.FromHexString("FEBEDEEF")), "请求头标识");
            var length = BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) & 0xFFFFFF;
            Expect(length == packet.Length - 10 && packet.Slice(10, sid.Length).SequenceEqual(sid), "请求载荷包含 SID");
            return DiarkisEncryption.Decrypt(packet[(10 + sid.Length)..], key, iv, macKey);
        }

        byte[] Response(byte version, ushort command, byte[] data)
        {
            var encrypted = DiarkisEncryption.Encrypt(data, key, iv, macKey);
            var bytes = new byte[11 + encrypted.Length];
            Convert.FromHexString("FEBEDEEF").CopyTo(bytes, 0);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4), encrypted.Length);
            bytes[4] = version;
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), command);
            encrypted.CopyTo(bytes, 11);
            return bytes;
        }

        static void Expect(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
        }
    }
}
