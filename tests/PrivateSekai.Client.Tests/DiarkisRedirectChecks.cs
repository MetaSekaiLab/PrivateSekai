using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using PrivateSekai.Client.Realtime;

internal static class DiarkisRedirectChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        foreach (var embedded in new[] { false, true }) await Redirect(embedded);
        foreach (var invalid in new[] { "", "host", ":80", "host:0", "host:65536", "host:80/other" })
        {
            var rejected = false;
            try { DiarkisPacket.ParseReconnect([0xBE, 0xEF, 0xFE, 0xED, .. Encoding.UTF8.GetBytes(invalid)]); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "连接切换拒绝不完整地址、非法端口或路径注入");
        }
        var badMagic = false;
        try { DiarkisPacket.ParseReconnect(Encoding.UTF8.GetBytes("xxxxlocalhost:80")); }
        catch (InvalidDataException) { badMagic = true; }
        check(badMagic, "连接切换必须包含原协议魔数");

        async Task Redirect(bool embedded)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var token = timeout.Token;
            using var first = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            using var second = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
            var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
            var mac = Enumerable.Range(32, 32).Select(i => (byte)i).ToArray();
            var sid = Enumerable.Range(64, 16).Select(i => (byte)i).ToArray();
            var authentication = new JsonObject
            {
                ["udpHost"] = "127.0.0.1", ["udpPort"] = ((IPEndPoint)first.Client.LocalEndPoint!).Port,
                ["clientKey"] = "synthetic-client-key", ["sid"] = Convert.ToHexString(sid),
                ["encryptionKey"] = Convert.ToHexString(key), ["encryptionIv"] = Convert.ToHexString(iv), ["encryptionMacKey"] = Convert.ToHexString(mac)
            };
            var oldPeer = OldPeer();
            var newPeer = NewPeer();
            using var client = new DiarkisUdpClient(authentication, false);
            await client.ConnectAsync(token);
            await client.FlushAsync(token);
            await client.WaitForEchoAsync(1, token);
            await client.SendReliableAsync(2, 3080, [11], token);
            var joined = await client.ReceiveAsync(token);
            await client.SendReliableAsync(2, 10010, [12], token);
            await client.FlushAsync(token);
            await Task.WhenAll(oldPeer, newPeer);
            check(joined.Command == 3001 && joined.Payload.SequenceEqual(new byte[] { 31 })
                && client.Redirects == 1 && client.ClientKeyAcknowledged && client.Retransmissions == 0,
                embedded ? "Echo内附切换通知触发跨端口握手并继续交付入房响应" : "控制推送触发跨端口握手并继续交付入房响应");

            async Task<(IPEndPoint Endpoint, byte[] Echo)> Handshake(UdpClient server, uint sequence)
            {
                var syn = await server.ReceiveAsync(token);
                var endpoint = syn.RemoteEndPoint;
                Expect(syn.Buffer.SequenceEqual(DiarkisPacket.CreateUdp(2, 0, sid)), "新旧节点SYN使用相同SID与零序号");
                await server.SendAsync(DiarkisPacket.CreateUdp(2, sequence, []), endpoint, token);
                var ack = await server.ReceiveAsync(token);
                Expect(ack.Buffer.SequenceEqual(DiarkisPacket.CreateUdp(4, sequence, sid)), "新节点ACK采用该节点的接收序号");
                var clientKey = await server.ReceiveAsync(token);
                Expect(DiarkisPacket.ParseUdp(clientKey.Buffer).Sequence == 0 && Command(clientKey.Buffer) == 4
                    && Decode(clientKey.Buffer).SequenceEqual(Encoding.UTF8.GetBytes("synthetic-client-key")), "重连重新验证原clientKey，发送序号归零");
                var echo = await server.ReceiveAsync(token);
                Expect(DiarkisPacket.ParseUdp(echo.Buffer).Sequence == 1 && Command(echo.Buffer) == 1, "新节点重置Echo发送序号");
                await server.SendAsync(DiarkisPacket.CreateUdp(4, 0, []), endpoint, token);
                await server.SendAsync(DiarkisPacket.CreateUdp(4, 1, []), endpoint, token);
                await server.SendAsync(DiarkisPacket.CreateUdp(1, 0, Response(0, 1, 1, Echo(Decode(echo.Buffer)))), endpoint, token);
                return (endpoint, Decode(echo.Buffer));
            }
            async Task OldPeer()
            {
                var (endpoint, echo) = await Handshake(first, 7);
                var request = await first.ReceiveAsync(token);
                Expect(Command(request.Buffer) == 3080 && DiarkisPacket.ParseUdp(request.Buffer).Sequence == 2, "初始节点只接收一次入房请求");
                await first.SendAsync(DiarkisPacket.CreateUdp(4, 2, []), endpoint, token);
                var destination = new byte[] { 0xBE, 0xEF, 0xFE, 0xED }.Concat(
                    Encoding.UTF8.GetBytes("127.0.0.1:" + ((IPEndPoint)second.Client.LocalEndPoint!).Port)).ToArray();
                var control = embedded ? Response(0, 1, 1, Echo(echo, destination)) : Response(0, 2, 255, destination);
                await first.SendAsync(DiarkisPacket.CreateUdp(3, 7, control), endpoint, token);
                var ack = await first.ReceiveAsync(token);
                Expect(ack.Buffer.SequenceEqual(DiarkisPacket.CreateUdp(4, 7, sid)), "切换前确认旧节点可靠控制消息");
                var fin = await first.ReceiveAsync(token);
                Expect(fin.Buffer.SequenceEqual(DiarkisPacket.CreateUdp(7, 3, sid)), "旧节点收到FIN及SID，不收到业务重发");
            }
            async Task NewPeer()
            {
                var (endpoint, _) = await Handshake(second, 81);
                await second.SendAsync(DiarkisPacket.CreateUdp(3, 81, Response(2, 3001, 1, [31])), endpoint, token);
                var ack = await second.ReceiveAsync(token);
                Expect(ack.Buffer.SequenceEqual(DiarkisPacket.CreateUdp(4, 81, sid)), "新节点的首个可靠响应正常交付和确认");
                var followup = await second.ReceiveAsync(token);
                Expect(Command(followup.Buffer) == 10010 && DiarkisPacket.ParseUdp(followup.Buffer).Sequence == 2,
                    "切换后直接继续下一条业务，不重复3080");
                await second.SendAsync(DiarkisPacket.CreateUdp(4, 2, []), endpoint, token);
            }
            static ushort Command(byte[] packet) => BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(12));
            byte[] Decode(byte[] packet) => DiarkisEncryption.Decrypt(packet.AsSpan(14 + sid.Length), key, iv, mac);
            byte[] Response(byte version, ushort command, byte status, byte[] plain)
            {
                var encrypted = DiarkisEncryption.Encrypt(plain, key, iv, mac);
                var result = new byte[11 + encrypted.Length];
                Convert.FromHexString("FEBEDEEF").CopyTo(result, 0);
                BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), encrypted.Length);
                result[4] = version;
                BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(8), command);
                result[10] = status;
                encrypted.CopyTo(result, 11);
                return result;
            }
            static byte[] Echo(byte[] request, byte[]? notification = null)
            {
                var address = Encoding.UTF8.GetBytes("127.0.0.1:1");
                var additional = notification == null ? [] : new byte[5 + notification.Length];
                if (notification != null)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(additional, (ushort)additional.Length);
                    BinaryPrimitives.WriteUInt16BigEndian(additional.AsSpan(3), 2);
                    notification.CopyTo(additional, 5);
                }
                var result = new byte[11 + address.Length + additional.Length];
                result[0] = 1;
                request.AsSpan(0, 8).CopyTo(result.AsSpan(1));
                address.CopyTo(result, 9);
                additional.CopyTo(result, 9 + address.Length);
                BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(result.Length - 2), (ushort)(9 + address.Length));
                return result;
            }
            static void Expect(bool condition, string label)
            {
                if (!condition) throw new InvalidOperationException(label);
            }
        }
    }
}
