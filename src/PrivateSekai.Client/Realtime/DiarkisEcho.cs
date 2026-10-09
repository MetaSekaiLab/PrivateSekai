using System.Buffers.Binary;
using System.Text;

namespace PrivateSekai.Client.Realtime;

public sealed class DiarkisEcho
{
    public byte State { get; init; }
    public bool IsOffline => State == 2;
    public double Timestamp { get; init; }
    public string Address { get; init; } = "";
    public IReadOnlyList<DiarkisResponse> Notifications { get; init; } = [];

    public static byte[] CreateRequest(double timestamp, IEnumerable<string> addresses)
    {
        using var stream = new MemoryStream();
        Span<byte> number = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(number, timestamp);
        stream.Write(number);
        foreach (var address in addresses)
        {
            var bytes = Encoding.UTF8.GetBytes(address);
            BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length);
            stream.Write(number[..4]);
            stream.Write(bytes);
        }
        return stream.ToArray();
    }

    public static DiarkisEcho Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 9) throw new InvalidDataException("Echo 响应缺少状态或时间戳。");
        var addressEnd = BinaryPrimitives.ReadUInt16BigEndian(payload[^2..]);
        var hasAdditionalData = addressEnd < payload.Length;
        var end = hasAdditionalData ? addressEnd : payload.Length;
        if (end < 9 || (hasAdditionalData && end > payload.Length - 2))
            throw new InvalidDataException("Echo 地址边界无效。");
        var notifications = new List<DiarkisResponse>();
        if (hasAdditionalData)
        {
            var offset = end;
            while (offset < payload.Length - 2)
            {
                var remaining = payload.Slice(offset, payload.Length - 2 - offset);
                if (remaining.Length < 5) throw new InvalidDataException("Echo 通知头不完整。");
                var length = BinaryPrimitives.ReadUInt16BigEndian(remaining);
                if (length < 5 || length > remaining.Length) throw new InvalidDataException("Echo 通知长度无效。");
                notifications.Add(new DiarkisResponse(remaining[2], BinaryPrimitives.ReadUInt16BigEndian(remaining[3..]),
                    255, remaining.Slice(5, length - 5).ToArray(), 0));
                offset += length;
            }
        }
        return new DiarkisEcho
        {
            State = payload[0], Timestamp = BinaryPrimitives.ReadDoubleLittleEndian(payload[1..]),
            Address = Encoding.UTF8.GetString(payload[9..end]), Notifications = notifications
        };
    }
}
