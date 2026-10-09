using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PrivateSekai.Client.Realtime;

public static class DiarkisEncryption
{
    private const int HeaderSize = 36;

    public static byte[] Encrypt(ReadOnlySpan<byte> payload, byte[] key, byte[] iv, byte[] macKey)
    {
        var padded = new byte[checked(payload.Length + 16 - payload.Length % 16)];
        payload.CopyTo(padded);
        using var aes = Aes.Create();
        aes.Key = key;
        var ciphertext = aes.EncryptCbc(padded, iv, PaddingMode.None);
        var packet = new byte[checked(HeaderSize + ciphertext.Length)];
        BinaryPrimitives.WriteInt32BigEndian(packet, payload.Length);
        HMACSHA256.HashData(macKey, ciphertext).CopyTo(packet, 4);
        ciphertext.CopyTo(packet, HeaderSize);
        return packet;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> packet, byte[] key, byte[] iv, byte[] macKey)
    {
        if (packet.Length < HeaderSize + 16 || (packet.Length - HeaderSize) % 16 != 0)
            throw new CryptographicException("实时消息长度无效。");
        var length = BinaryPrimitives.ReadInt32BigEndian(packet);
        var ciphertext = packet[HeaderSize..];
        if (length < 0 || length > ciphertext.Length)
            throw new CryptographicException("实时消息原文长度越界。");
        var mac = HMACSHA256.HashData(macKey, ciphertext);
        if (!CryptographicOperations.FixedTimeEquals(packet.Slice(4, 32), mac))
            throw new CryptographicException("实时消息认证失败。");
        using var aes = Aes.Create();
        aes.Key = key;
        var plaintext = aes.DecryptCbc(ciphertext, iv, PaddingMode.None);
        return plaintext.AsSpan(0, length).ToArray();
    }
}
