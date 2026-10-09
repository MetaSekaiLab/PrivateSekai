using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace PrivateSekai.Client.Realtime;

// 单连接、串行调用；ReceiveAsync 同时推进接收、确认和重传。
public sealed class DiarkisUdpClient : IDisposable
{
    private readonly UdpClient socket;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly string host;
    private readonly int port;
    private readonly string clientKey;
    private readonly byte[] sid;
    private readonly byte[] key;
    private readonly byte[] iv;
    private readonly byte[] macKey;
    private readonly bool legacySplit;
    private readonly Dictionary<uint, Retry> pending = [];
    private readonly Dictionary<uint, byte[]> incoming = [];
    private readonly Dictionary<short, DiarkisSplitPacket> fragments = [];
    private readonly Queue<DiarkisResponse> responses = [];
    private uint sendSequence;
    private uint receiveSequence;
    private short splitId;
    private bool started;
    private bool connected;
    private bool clientKeySent;

    public int SentDatagrams { get; private set; }
    public int ReceivedDatagrams { get; private set; }
    public int Retransmissions { get; private set; }
    public bool ClientKeyAcknowledged => clientKeySent && !pending.ContainsKey(0);

    public DiarkisUdpClient(JsonObject authentication, bool legacySplit)
    {
        host = Required(authentication, "udpHost");
        port = authentication["udpPort"]?.GetValue<int>() ?? 0;
        if (port is < 1 or > 65535) throw new InvalidDataException("UDP 端口无效。");
        clientKey = Required(authentication, "clientKey");
        sid = Convert.FromHexString(Required(authentication, "sid"));
        key = Convert.FromHexString(Required(authentication, "encryptionKey"));
        iv = Convert.FromHexString(Required(authentication, "encryptionIv"));
        macKey = Convert.FromHexString(Required(authentication, "encryptionMacKey"));
        _ = DiarkisEncryption.Encrypt([], key, iv, macKey);
        this.legacySplit = legacySplit;
        socket = new UdpClient(AddressFamily.InterNetworkV6);
        socket.Client.DualMode = true;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (started) throw new InvalidOperationException("实时连接实例不能重复连接。");
        started = true;
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var address = addresses.First();
        socket.Connect(new IPEndPoint(address.MapToIPv6(), port));
        while (!connected)
        {
            await SendDatagram(DiarkisPacket.CreateUdp(2, 0, sid), cancellationToken);
            if (await ReadDatagram(cancellationToken) is { } datagram)
                await ProcessDatagram(datagram, cancellationToken);
        }
        await SendReliableAsync(0, 4, Encoding.UTF8.GetBytes(clientKey), cancellationToken);
        clientKeySent = true;
    }

    public async Task SendReliableAsync(byte version, ushort command, byte[] payload, CancellationToken cancellationToken)
    {
        RequireConnected();
        var packets = DiarkisPacket.CreateReliableRequests(version, command, payload, sid, key, iv, macKey, splitId);
        if (sendSequence + packets.Count > 0x1000000L)
            throw new InvalidOperationException("实时序号已耗尽，需要重新连接。");
        if (packets.Count > 1) splitId = splitId == 32766 ? (short)0 : (short)(splitId + 1);
        foreach (var packet in packets)
        {
            var sequence = sendSequence++;
            await SendDatagram(DiarkisPacket.CreateUdp(3, sequence, packet), cancellationToken);
            pending.Add(sequence, new Retry(DiarkisPacket.CreateUdp(5, sequence, packet), clock.ElapsedMilliseconds));
        }
    }

    public async Task SendUnreliableAsync(byte version, ushort command, byte[] payload, CancellationToken cancellationToken)
    {
        RequireConnected();
        var request = DiarkisPacket.CreateEncryptedRequest(version, command, payload, sid, key, iv, macKey);
        var packet = DiarkisPacket.CreateUdp(1, 0, request);
        if (packet.Length > 1300) throw new InvalidOperationException("普通 UDP 数据报超过 1300 字节。");
        await SendDatagram(packet, cancellationToken);
    }

    public async Task<DiarkisResponse> ReceiveAsync(CancellationToken cancellationToken)
    {
        RequireConnected();
        while (responses.Count == 0)
            await Pump(cancellationToken);
        return responses.Dequeue();
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        RequireConnected();
        while (pending.Count > 0) await Pump(cancellationToken);
    }

    private async Task Pump(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var retry in pending.Values)
        {
            if (clock.ElapsedMilliseconds - retry.SentAt < 1000) continue;
            if (retry.Count == 10) throw new TimeoutException("实时消息未得到确认。");
            await SendDatagram(retry.Packet, cancellationToken);
            retry.SentAt = clock.ElapsedMilliseconds;
            retry.Count++;
            Retransmissions++;
        }
        if (await ReadDatagram(cancellationToken) is { } datagram)
            await ProcessDatagram(datagram, cancellationToken);
    }

    private async Task ProcessDatagram(byte[] datagram, CancellationToken cancellationToken)
    {
        var work = new Queue<byte[]>();
        work.Enqueue(datagram);
        while (work.TryDequeue(out var bytes))
        {
            if (bytes.AsSpan().SequenceEqual(new byte[] { 13, 14, 10, 13 }))
                throw new IOException("实时服务端终止连接。");
            var packet = DiarkisPacket.ParseUdp(bytes);
            switch (packet.Flag)
            {
                case 2:
                    Initialize(packet.Sequence);
                    await Acknowledge(4, packet.Sequence, cancellationToken);
                    break;
                case 4:
                case 6:
                    Initialize(packet.Sequence);
                    pending.Remove(packet.Sequence);
                    break;
                case 1:
                    Deliver(packet.Payload, work);
                    break;
                case 3:
                case 5:
                    if (!connected) throw new InvalidDataException("握手前收到可靠业务消息。");
                    await Acknowledge(packet.Flag == 3 ? (byte)4 : (byte)6, packet.Sequence, cancellationToken);
                    if (packet.Sequence < receiveSequence || incoming.ContainsKey(packet.Sequence)) break;
                    var payload = packet.Payload;
                    if (DiarkisSplitPacket.IsSplitPacket(payload))
                    {
                        var id = System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(payload.AsSpan(4));
                        if (fragments.TryGetValue(id, out var split)) split.Add(payload);
                        else fragments.Add(id, split = new DiarkisSplitPacket(payload, legacySplit));
                        payload = split.GetBytes();
                        if (split.IsComplete) fragments.Remove(id);
                    }
                    incoming.Add(packet.Sequence, payload);
                    while (incoming.Remove(receiveSequence, out var ordered))
                    {
                        receiveSequence++;
                        if (ordered.Length > 0) Deliver(ordered, work);
                    }
                    break;
                case 7:
                    await Acknowledge(4, receiveSequence, cancellationToken);
                    connected = false;
                    throw new IOException("实时服务端关闭连接。");
                default:
                    throw new InvalidDataException("未支持的实时传输标志。");
            }
        }
    }

    private void Deliver(byte[] packet, Queue<byte[]> work)
    {
        if (packet.Length == 0) return;
        var response = DiarkisPacket.ParseResponse(packet);
        var payload = DiarkisEncryption.Decrypt(response.Payload, key, iv, macKey);
        responses.Enqueue(response with { Payload = payload });
        if (response.ConsumedSize < packet.Length)
            work.Enqueue(packet[response.ConsumedSize..]);
    }

    private void Initialize(uint sequence)
    {
        if (connected) return;
        connected = true;
        receiveSequence = sequence;
    }

    private Task Acknowledge(byte flag, uint sequence, CancellationToken cancellationToken) =>
        SendDatagram(DiarkisPacket.CreateUdp(flag, sequence, sid), cancellationToken);

    private async Task SendDatagram(byte[] packet, CancellationToken cancellationToken)
    {
        await socket.SendAsync(packet, cancellationToken);
        SentDatagrams++;
    }

    private async Task<byte[]?> ReadDatagram(CancellationToken cancellationToken)
    {
        using var poll = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        poll.CancelAfter(16);
        try
        {
            var packet = await socket.ReceiveAsync(poll.Token);
            ReceivedDatagrams++;
            return packet.Buffer;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private void RequireConnected()
    {
        if (!connected) throw new InvalidOperationException("实时连接尚未建立。");
    }

    private static string Required(JsonObject authentication, string name) =>
        authentication[name]?.GetValue<string>() is { Length: > 0 } value
            ? value : throw new InvalidDataException("实时认证字段缺失。");

    public void Dispose()
    {
        connected = false;
        socket.Dispose();
    }

    private sealed class Retry(byte[] packet, long sentAt)
    {
        public byte[] Packet { get; } = packet;
        public long SentAt { get; set; } = sentAt;
        public int Count { get; set; }
    }
}
