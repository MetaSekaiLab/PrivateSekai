using System.Buffers.Binary;

namespace PrivateSekai.Client.Realtime;

// 一个实例收集一个分片组；调用方按组 ID 分派，并串行调用 Add。
public sealed class DiarkisSplitPacket
{
    private const uint HeaderSymbol = 0xFFFEFDFC;
    private readonly bool legacyInsert;
    private readonly List<byte[]> parts;
    private readonly bool[] seen;
    private int receivedCount;
    private int totalSize;

    public short Id { get; }
    public int Count => seen.Length;
    public bool IsComplete => receivedCount == Count;

    public DiarkisSplitPacket(ReadOnlySpan<byte> firstPacket, bool legacyInsert)
    {
        var header = ReadHeader(firstPacket);
        Id = header.Id;
        this.legacyInsert = legacyInsert;
        seen = new bool[header.Count];
        parts = Enumerable.Range(0, header.Count).Select(_ => Array.Empty<byte>()).ToList();
        Add(firstPacket);
    }

    public static bool IsSplitPacket(ReadOnlySpan<byte> packet) =>
        packet.Length >= 11 && BinaryPrimitives.ReadUInt32BigEndian(packet) == HeaderSymbol;

    public static IReadOnlyList<byte[]> Create(short id, ReadOnlySpan<byte> payload, int splitSize)
    {
        if (splitSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(splitSize));
        var count = payload.Length == 0 ? 0 : (payload.Length - 1) / splitSize + 1;
        if (count > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(splitSize), "分片数量超过有符号 16 位范围。");
        var result = new List<byte[]>(count);
        for (var index = 0; index < count; index++)
        {
            var offset = index * splitSize;
            var size = Math.Min(splitSize, payload.Length - offset);
            var packet = new byte[checked(10 + size)];
            BinaryPrimitives.WriteUInt32BigEndian(packet, HeaderSymbol);
            BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(4), id);
            BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(6), (short)index);
            BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(8), (short)count);
            payload.Slice(offset, size).CopyTo(packet.AsSpan(10));
            result.Add(packet);
        }
        return result;
    }

    public bool Add(ReadOnlySpan<byte> packet)
    {
        var header = ReadHeader(packet);
        if (header.Id != Id)
            return false;
        if (header.Count != Count)
            throw new InvalidDataException("同一分片组的总数不一致。");
        if (seen[header.Index])
            return false;
        var newSize = checked(totalSize + packet.Length - 10);
        var payload = packet[10..].ToArray();
        // 旧版使用 Insert，保留其乱序时与按索引赋值不同的行为。
        if (legacyInsert)
            parts.Insert(header.Index, payload);
        else
            parts[header.Index] = payload;
        seen[header.Index] = true;
        receivedCount++;
        totalSize = newSize;
        return true;
    }

    public byte[] GetBytes()
    {
        if (!IsComplete)
            return [];
        var result = new byte[totalSize];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }

    private static (short Id, short Index, short Count) ReadHeader(ReadOnlySpan<byte> packet)
    {
        if (!IsSplitPacket(packet))
            throw new InvalidDataException("分片头无效或载荷为空。");
        var id = BinaryPrimitives.ReadInt16BigEndian(packet[4..]);
        var index = BinaryPrimitives.ReadInt16BigEndian(packet[6..]);
        var count = BinaryPrimitives.ReadInt16BigEndian(packet[8..]);
        if (count <= 0 || index < 0 || index >= count)
            throw new InvalidDataException("分片索引或总数无效。");
        return (id, index, count);
    }
}
