extern alias game;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrivateSekai.Config;
using PrivateSekai.Modules.Live;
using PrivateSekai.Protocol;

namespace PrivateSekai.Transport.Realtime;

public sealed class DiarkisServer(RealtimeConfig config, MultiLiveRooms rooms, TimeProvider clock,
    ILogger<DiarkisServer> logger) : BackgroundService
{
    private readonly object gate = new();
    private readonly Dictionary<string, Peer> peers = [];
    private readonly Queue<(IPEndPoint Endpoint, byte[] Bytes)> outgoing = [];
    private UdpClient? socket;
    public int BoundPort { get; private set; }

    public byte[] Authenticate(long userId)
    {
        lock (gate)
        {
            if (socket == null) throw new NotSupportedException("实时服务尚未启用。");
            var peer = new Peer(userId, clock.GetUtcNow());
            var response = new UserDiarkisAuthResponse
            {
                userId = userId, clientKey = peer.ClientKey, sid = peer.Sid,
                udpHost = config.AdvertisedHost, udpPort = BoundPort, tcpHost = "", tcpPort = 0,
                encryptionKey = Convert.ToHexString(peer.Key), encryptionIv = Convert.ToHexString(peer.Iv),
                encryptionMacKey = Convert.ToHexString(peer.MacKey)
            };
            var encoded = DumpSerializer.Serialize(response);
            foreach (var old in peers.Values.Where(p => p.UserId == userId).ToArray()) Drop(old, true);
            if (peers.Count >= 256) throw new RoomConflictException("实时连接容量已满。");
            peers.Add(peer.Sid, peer);
            return encoded;
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (config.Enabled)
        {
            if (config.IdleTimeoutSeconds < 1 || config.Port is < 0 or > 65535
                || string.IsNullOrWhiteSpace(config.AdvertisedHost))
                throw new InvalidOperationException("实时服务配置无效。");
            socket = new UdpClient(new IPEndPoint(IPAddress.Parse(config.ListenAddress), config.Port));
            BoundPort = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        }
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (socket == null) return;
        try
        {
            var receiving = Read(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            var ticking = timer.WaitForNextTickAsync(stoppingToken).AsTask();
            while (!stoppingToken.IsCancellationRequested)
            {
                var ready = await Task.WhenAny(receiving, ticking);
                if (ready == receiving)
                {
                    var packet = await receiving;
                    lock (gate) Receive(packet);
                    receiving = Read(stoppingToken);
                }
                else
                {
                    if (!await ticking) break;
                    lock (gate) Tick();
                    ticking = timer.WaitForNextTickAsync(stoppingToken).AsTask();
                }
                (IPEndPoint Endpoint, byte[] Bytes)[] sending;
                lock (gate)
                {
                    sending = outgoing.ToArray();
                    outgoing.Clear();
                }
                foreach (var packet in sending)
                {
                    try { await socket.SendAsync(packet.Bytes, packet.Endpoint, stoppingToken); }
                    catch (SocketException error) when (error.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused)
                    {
                        logger.LogDebug("实时消息目标已断开。");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            lock (gate)
            {
                foreach (var peer in peers.Values.ToArray()) Drop(peer, false);
                outgoing.Clear();
                socket.Dispose();
                socket = null;
                BoundPort = 0;
            }
        }
    }

    private async Task<UdpReceiveResult> Read(CancellationToken token)
    {
        while (true)
        {
            try { return await socket!.ReceiveAsync(token); }
            catch (SocketException error) when (error.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused)
            {
                logger.LogDebug("实时消息目标已断开。");
            }
        }
    }

    private void Receive(UdpReceiveResult datagram)
    {
        Peer? peer = null;
        try
        {
            var udp = DiarkisPacket.ParseUdp(datagram.Buffer);
            var body = udp.Flag is 1 or 3 or 5 ? DiarkisPacket.ParseRequest(udp.Payload).Payload : udp.Payload;
            if (body.Length < 16 || !peers.TryGetValue(Convert.ToHexString(body.AsSpan(0, 16)), out peer)) return;
            if (udp.Flag == 2)
            {
                if (body.Length != 16) return;
                if (peer.Authenticated)
                {
                    if (rooms.HasPlayer(peer.UserId)) return;
                    peer.Authenticated = false;
                    peer.ReceiveSequence = peer.SendSequence = 0;
                    peer.SplitId = 0;
                    peer.Incoming.Clear();
                    peer.Pending.Clear();
                    peer.Fragments.Clear();
                }
                else if (peer.Endpoint != null && !peer.Endpoint.Equals(datagram.RemoteEndPoint)) return;
                peer.Endpoint = datagram.RemoteEndPoint;
                peer.LastSeen = clock.GetUtcNow();
                Send(peer, DiarkisPacket.CreateUdp(2, 0, []));
                return;
            }
            if (peer.Endpoint == null || !peer.Endpoint.Equals(datagram.RemoteEndPoint)) return;
            peer.LastSeen = clock.GetUtcNow();
            if (udp.Flag is 4 or 6)
            {
                if (body.Length == 16) peer.Pending.Remove(udp.Sequence);
                return;
            }
            if (udp.Flag == 7)
            {
                Send(peer, DiarkisPacket.CreateUdp(4, udp.Sequence, []));
                Drop(peer, false);
                return;
            }
            if (udp.Flag == 1)
            {
                var request = DiarkisPacket.ParseRequest(udp.Payload);
                if (request.Version != 0 || request.Command != 3) throw new InvalidDataException("非可靠数据报仅支持 Ping。");
                Dispatch(peer, udp.Payload);
                return;
            }
            if (udp.Flag is not (3 or 5)) return;
            if (udp.Sequence >= peer.ReceiveSequence + 128) throw new InvalidDataException("可靠消息接收窗口已满。");
            Send(peer, DiarkisPacket.CreateUdp(udp.Flag == 3 ? (byte)4 : (byte)6, udp.Sequence, []));
            if (udp.Sequence < peer.ReceiveSequence || !peer.Incoming.TryAdd(udp.Sequence, udp.Payload)) return;
            while (peer.Incoming.Remove(peer.ReceiveSequence, out var ordered))
            {
                peer.ReceiveSequence++;
                var request = DiarkisPacket.ParseRequest(ordered);
                var fragment = request.Payload.AsSpan(16);
                if (DiarkisSplitPacket.IsSplitPacket(fragment))
                {
                    if (fragment.Length < 10 || BinaryPrimitives.ReadInt16BigEndian(fragment[8..]) is < 1 or > 128)
                        throw new InvalidDataException("分片数量无效。");
                    var id = BinaryPrimitives.ReadInt16BigEndian(fragment[4..]);
                    if (peer.Fragments.TryGetValue(id, out var split)) split.Add(fragment.ToArray());
                    else
                    {
                        if (peer.Fragments.Count >= 8) throw new InvalidDataException("未完成分片组过多。");
                        peer.Fragments.Add(id, split = new DiarkisSplitPacket(fragment.ToArray(), false));
                    }
                    if (!split.IsComplete) continue;
                    ordered = split.GetBytes();
                    peer.Fragments.Remove(id);
                }
                Dispatch(peer, ordered);
                if (!peers.ContainsKey(peer.Sid)) break;
            }
        }
        catch (Exception error) when (error is InvalidDataException or CryptographicException or MessagePackSerializationException
            or NotSupportedException or KeyNotFoundException or UnauthorizedAccessException or RoomConflictException or ArgumentException or OverflowException)
        {
            // UDP 输入边界只记异常类型，不记录报文、身份、密钥或房间标识。
            logger.LogDebug("实时消息被拒绝：{ErrorType}", error.GetType().Name);
            if (peer != null) Drop(peer, true);
        }
    }

    private void Dispatch(Peer peer, byte[] packet)
    {
        var request = DiarkisPacket.ParseRequest(packet);
        if (request.Payload.Length < 16 || !request.Payload.AsSpan(0, 16).SequenceEqual(Convert.FromHexString(peer.Sid)))
            throw new InvalidDataException("实时消息身份不一致。");
        var payload = DiarkisEncryption.Decrypt(request.Payload.AsSpan(16), peer.Key, peer.Iv, peer.MacKey);
        if (request.Version == 0 && request.Command == 4)
        {
            if (!CryptographicOperations.FixedTimeEquals(payload, Encoding.UTF8.GetBytes(peer.ClientKey)))
                throw new InvalidDataException("实时连接认证失败。");
            peer.Authenticated = true;
            return;
        }
        if (!peer.Authenticated) throw new InvalidDataException("实时连接尚未认证。");
        if (request.Version == 0 && request.Command is 1 or 3)
        {
            if (payload.Length < 8 || request.Command == 3 && payload.Length != 8)
                throw new InvalidDataException("时钟请求长度无效。");
            var address = request.Command == 1 ? Encoding.UTF8.GetBytes(peer.Endpoint!.ToString()) : [];
            var response = new byte[9 + address.Length + (request.Command == 1 ? 2 : 0)];
            response[0] = 1;
            payload.AsSpan(0, 8).CopyTo(response.AsSpan(1));
            address.CopyTo(response, 9);
            if (request.Command == 1) BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(response.Length - 2), (ushort)(9 + address.Length));
            Reply(peer, 0, request.Command, 1, response, false);
            return;
        }
        Deliver(rooms.Process(peer.UserId, request.Version, request.Command, payload));
    }

    private void Deliver(IReadOnlyList<RoomDelivery> deliveries)
    {
        foreach (var delivery in deliveries)
        {
            var peer = peers.Values.SingleOrDefault(p => p.UserId == delivery.UserId);
            if (peer?.Endpoint == null) continue;
            if (delivery.Disconnect) Drop(peer, true);
            else Reply(peer, delivery.Version, delivery.Command, delivery.Status, delivery.Payload, true);
        }
    }

    private void Reply(Peer peer, byte version, ushort command, byte status, byte[] payload, bool reliable)
    {
        var packet = DiarkisPacket.CreateResponse(version, command, status, DiarkisEncryption.Encrypt(payload, peer.Key, peer.Iv, peer.MacKey));
        if (!reliable)
        {
            Send(peer, DiarkisPacket.CreateUdp(1, 0, packet));
            return;
        }
        var packets = packet.Length > 1300 ? DiarkisSplitPacket.Create(peer.SplitId, packet, 1300) : new[] { packet };
        if (packets.Count > 1) peer.SplitId = peer.SplitId == 32766 ? (short)0 : (short)(peer.SplitId + 1);
        if (peer.Pending.Count + packets.Count > 128) throw new InvalidDataException("未确认的可靠消息过多。");
        foreach (var part in packets)
        {
            if (peer.SendSequence > 0xFFFFFF) throw new InvalidDataException("可靠消息序号已耗尽。");
            var sequence = peer.SendSequence++;
            Send(peer, DiarkisPacket.CreateUdp(3, sequence, part));
            peer.Pending.Add(sequence, new Pending(DiarkisPacket.CreateUdp(5, sequence, part), clock.GetUtcNow()));
        }
    }

    private void Tick()
    {
        var now = clock.GetUtcNow();
        foreach (var peer in peers.Values.ToArray())
        {
            if (!peers.ContainsKey(peer.Sid)) continue;
            if ((now - peer.LastSeen).TotalSeconds >= config.IdleTimeoutSeconds || peer.Pending.Values.Any(p => p.Retries >= 10))
            {
                Drop(peer, true);
                continue;
            }
            foreach (var pending in peer.Pending.Values)
            {
                if ((now - pending.SentAt).TotalSeconds < 1) continue;
                Send(peer, pending.Bytes);
                pending.SentAt = now;
                pending.Retries++;
            }
        }
        rooms.Prune();
    }

    private void Drop(Peer peer, bool notify)
    {
        if (!peers.Remove(peer.Sid)) return;
        if (notify && peer.Endpoint != null) Send(peer, [13, 14, 10, 13]);
        Deliver(rooms.Disconnect(peer.UserId));
    }

    private void Send(Peer peer, byte[] bytes) => outgoing.Enqueue((peer.Endpoint!, bytes));

    private sealed class Peer(long userId, DateTimeOffset now)
    {
        public long UserId = userId;
        public string Sid = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        public string ClientKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        public byte[] Key = RandomNumberGenerator.GetBytes(16);
        public byte[] Iv = RandomNumberGenerator.GetBytes(16);
        public byte[] MacKey = RandomNumberGenerator.GetBytes(32);
        public IPEndPoint? Endpoint;
        public bool Authenticated;
        public DateTimeOffset LastSeen = now;
        public uint ReceiveSequence;
        public uint SendSequence;
        public short SplitId;
        public Dictionary<uint, byte[]> Incoming = [];
        public Dictionary<short, DiarkisSplitPacket> Fragments = [];
        public Dictionary<uint, Pending> Pending = [];
    }

    private sealed class Pending(byte[] bytes, DateTimeOffset sentAt)
    {
        public byte[] Bytes = bytes;
        public DateTimeOffset SentAt = sentAt;
        public int Retries;
    }
}
