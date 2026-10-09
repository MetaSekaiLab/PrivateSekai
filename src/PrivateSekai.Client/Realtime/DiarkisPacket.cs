using System.Buffers.Binary;

namespace PrivateSekai.Client.Realtime;

public static class DiarkisPacket
{
    private const uint HeaderSymbol = 0xFEBEDEEF;
    private const int MaxPayloadLength = 0xFFFFFF;

    public static byte[] CreateRequest(byte version, ushort command, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayloadLength)
            throw new ArgumentOutOfRangeException(nameof(payload), "实时消息载荷超过 24 位长度。");
        var packet = new byte[10 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(packet, HeaderSymbol);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4), payload.Length);
        packet[4] = version;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(8), command);
        payload.CopyTo(packet.AsSpan(10));
        return packet;
    }

    public static byte[] CreateEncryptedRequest(byte version, ushort command, ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> sid, byte[] key, byte[] iv, byte[] macKey)
    {
        var encrypted = DiarkisEncryption.Encrypt(payload, key, iv, macKey);
        var securePayload = new byte[checked(sid.Length + encrypted.Length)];
        sid.CopyTo(securePayload);
        encrypted.CopyTo(securePayload, sid.Length);
        return CreateRequest(version, command, securePayload);
    }

    // 响应比请求多一个状态字节；返回消费长度，剩余数据交给传输层处理。
    public static DiarkisResponse ParseResponse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 11)
            throw new InvalidDataException("实时响应头不完整。");
        if (BinaryPrimitives.ReadUInt32BigEndian(packet) != HeaderSymbol)
            throw new InvalidDataException("实时响应头标识无效。");
        var length = (int)(BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) & MaxPayloadLength);
        if (length > packet.Length - 11)
            throw new InvalidDataException("实时响应载荷不完整。");
        return new DiarkisResponse(packet[4], BinaryPrimitives.ReadUInt16BigEndian(packet[8..]),
            packet[10], packet.Slice(11, length).ToArray(), 11 + length);
    }

    public static byte[] CreateUdp(byte flag, uint sequence, ReadOnlySpan<byte> payload)
    {
        if (sequence > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(sequence), "UDP 序号超过 24 位。");
        var packet = new byte[checked(4 + payload.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, sequence);
        packet[3] = flag;
        payload.CopyTo(packet.AsSpan(4));
        return packet;
    }

    public static DiarkisUdpPacket ParseUdp(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 4)
            throw new InvalidDataException("UDP 消息头不完整。");
        return new DiarkisUdpPacket(packet[3], BinaryPrimitives.ReadUInt32LittleEndian(packet) & 0xFFFFFF,
            packet[4..].ToArray());
    }
}

public sealed record DiarkisResponse(byte Version, ushort Command, byte Status, byte[] Payload, int ConsumedSize)
{
    public bool IsPush => Status == 255;
}

public sealed record DiarkisUdpPacket(byte Flag, uint Sequence, byte[] Payload)
{
    public bool IsReliable => Flag != 1;
}
