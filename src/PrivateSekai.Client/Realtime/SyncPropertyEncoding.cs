using System.Buffers.Binary;
using System.Text;
using PrivateSekai.Protocol;

namespace PrivateSekai.Client.Realtime;

public static class SyncPropertyEncoding
{
    public static byte[] Byte(byte value) => Wrap(1, [value]);

    public static byte[] Int32(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return Wrap(2, bytes);
    }

    public static byte[] Int64(long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        return Wrap(3, bytes);
    }

    public static byte[] String(string value) => Wrap(4, Encoding.UTF8.GetBytes(value));

    public static byte[] Float(float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, value);
        return Wrap(5, bytes);
    }

    public static byte[] Boolean(bool value) => Wrap(6, [value ? (byte)1 : (byte)0]);

    // 对象载荷由其 MessagePack 契约编码，外层仍保留同步属性头。
    public static byte[] Object(ReadOnlySpan<byte> messagePack) => Wrap(7, messagePack);

    public static int ReadInt32(byte[] value)
    {
        var payload = ReadPayload(value, 2);
        if (payload.Length != 4) throw new InvalidDataException("整数同步属性长度无效。");
        return BinaryPrimitives.ReadInt32BigEndian(payload.Span);
    }

    public static long ReadInt64(byte[] value)
    {
        var payload = ReadPayload(value, 3);
        if (payload.Length != 8) throw new InvalidDataException("长整数同步属性长度无效。");
        return BinaryPrimitives.ReadInt64BigEndian(payload.Span);
    }

    public static string ReadString(byte[] value) => Encoding.UTF8.GetString(ReadPayload(value, 4).Span);
    public static T ReadObject<T>(byte[] value) => DumpSerializer.Deserialize<T>(ReadPayload(value, 7));

    private static ReadOnlyMemory<byte> ReadPayload(byte[] value, byte type)
    {
        if (value.Length < 5 || value[0] != type || BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(1)) != value.Length - 5)
            throw new InvalidDataException("同步属性类型或长度无效。");
        return value.AsMemory(5);
    }

    private static byte[] Wrap(byte type, ReadOnlySpan<byte> payload)
    {
        var result = new byte[checked(5 + payload.Length)];
        result[0] = type;
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(1), payload.Length);
        payload.CopyTo(result.AsSpan(5));
        return result;
    }
}
