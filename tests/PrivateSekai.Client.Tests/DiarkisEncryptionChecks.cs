using System.Security.Cryptography;
using PrivateSekai.Client.Realtime;

internal static class DiarkisEncryptionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var macKey = Enumerable.Range(32, 32).Select(i => (byte)i).ToArray();
        // 使用独立 Python cryptography AES-CBC 与标准库 HMAC 生成的固定向量。
        var vectors = new[]
        {
            (Array.Empty<byte>(), "00000000EEDC6BF8345930F2C0AC86647FA7085F1630D2A43B5CD1492CA8285AEAFE9D1907FEEF74E1D5036E900EEE118E949293"),
            (key, "0000001094F08CC3EFD69C890F3EA1A00671B33FF169A196E36A5E52B577D76737F46836954F64F2E4E86E9EEE82D20216684899644DD2FB9700306E76D21EC9F1BB00B0"),
            (key.Concat(new byte[] { 0 }).ToArray(), "0000001194F08CC3EFD69C890F3EA1A00671B33FF169A196E36A5E52B577D76737F46836954F64F2E4E86E9EEE82D20216684899644DD2FB9700306E76D21EC9F1BB00B0")
        };
        foreach (var (payload, hex) in vectors)
        {
            var expected = Convert.FromHexString(hex);
            check(DiarkisEncryption.Encrypt(payload, key, iv, macKey).SequenceEqual(expected), "实时加密匹配独立向量，覆盖空原文、整块补零和尾部零");
            check(DiarkisEncryption.Decrypt(expected, key, iv, macKey).SequenceEqual(payload), "实时解密按长度恢复原文，保留有效尾部零");
        }
        var valid = Convert.FromHexString(vectors[1].Item2);
        foreach (var index in new[] { 4, 36 })
        {
            var changed = valid.ToArray();
            changed[index] ^= 1;
            Reject(changed, "实时消息拒绝被修改的认证码或密文");
        }
        Reject(valid[..35], "实时消息拒绝不完整头部");
        Reject(valid[..^1], "实时消息拒绝非整块密文");
        var negative = valid.ToArray();
        negative[0] = 255;
        Reject(negative, "实时消息拒绝负原文长度");
        var oversized = valid.ToArray();
        oversized[3] = 127;
        Reject(oversized, "实时消息拒绝越界原文长度");

        void Reject(byte[] packet, string message)
        {
            var rejected = false;
            try { DiarkisEncryption.Decrypt(packet, key, iv, macKey); }
            catch (CryptographicException) { rejected = true; }
            check(rejected, message);
        }
    }
}
