using PrivateSekai.Client.Realtime;

internal static class DiarkisPacketChecks
{
    public static void Run(Action<bool, string> check)
    {
        // 固定样本按逆向字段布局手工排列，不用被测编码器生成解析输入。
        check(DiarkisPacket.CreateRequest(2, 3060, [0xA1, 0xB2, 0xC3])
            .SequenceEqual(Convert.FromHexString("FEBEDEEF020000030BF4A1B2C3")), "业务请求使用 10 字节头、大端长度和命令");
        check(DiarkisPacket.CreateRequest(255, 65535, [])
            .SequenceEqual(Convert.FromHexString("FEBEDEEFFF000000FFFF")), "业务请求支持空载荷及完整版本和命令范围");
        var large = DiarkisPacket.CreateRequest(7, 0x1234, new byte[0x010203]);
        check(large.AsSpan(0, 10).SequenceEqual(Convert.FromHexString("FEBEDEEF070102031234"))
            && large.Length == 10 + 0x010203, "业务长度覆盖全部三字节，不与版本混淆");

        var response = DiarkisPacket.ParseResponse(Convert.FromHexString("FEBEDEEF020000030BF4FFA1B2C39988"));
        check(response.Version == 2 && response.Command == 3060 && response.Status == 255 && response.IsPush
            && response.ConsumedSize == 14 && response.Payload.SequenceEqual(new byte[] { 0xA1, 0xB2, 0xC3 }),
            "响应独立读取状态字节，消费长度不吞掉后续传输数据");
        var empty = DiarkisPacket.ParseResponse(Convert.FromHexString("FEBEDEEFFF000000FFFF00"));
        check(empty.Version == 255 && empty.Command == 65535 && !empty.IsPush && empty.Payload.Length == 0
            && empty.ConsumedSize == 11, "响应支持空载荷及零状态");
        foreach (var status in new byte[] { 1, 127, 254 })
        {
            var bytes = Convert.FromHexString("FEBEDEEF02000000000100");
            bytes[10] = status;
            var parsed = DiarkisPacket.ParseResponse(bytes);
            check(parsed.Status == status && !parsed.IsPush, "响应保留未知业务状态，不擅自当作成功");
        }
        Reject(() => DiarkisPacket.ParseResponse(new byte[10]), "拒绝缺少状态字节的响应");
        Reject(() => DiarkisPacket.ParseResponse(Convert.FromHexString("FFBEDEEF02000000000100")), "拒绝错误业务头标识");
        Reject(() => DiarkisPacket.ParseResponse(Convert.FromHexString("FEBEDEEF020000030BF400A1B2")), "拒绝截断载荷");
        Reject(() => DiarkisPacket.ParseResponse(Convert.FromHexString("FEBEDEEF02FFFFFF0BF400")), "声明超长载荷时先校验可读范围");

        check(DiarkisPacket.CreateUdp(3, 0x123456, [0xAA, 0xBB])
            .SequenceEqual(Convert.FromHexString("56341203AABB")), "UDP 使用小端 24 位序号及独立标志");
        check(DiarkisPacket.CreateUdp(2, 0, [0x10, 0x20])
            .SequenceEqual(Convert.FromHexString("000000021020")), "SYN 直接携带 SID，不套业务头或加密层");
        foreach (var flag in new byte[] { 1, 2, 3, 4, 5, 6, 7, 255 })
        {
            var bytes = Convert.FromHexString("FFFFFF00AABB");
            bytes[3] = flag;
            var parsed = DiarkisPacket.ParseUdp(bytes);
            check(parsed.Flag == flag && parsed.Sequence == 0xFFFFFF && parsed.IsReliable == (flag != 1)
                && parsed.Payload.SequenceEqual(new byte[] { 0xAA, 0xBB }), "UDP 保留标志与最大序号，普通 UDP 之外交由可靠层处理");
        }
        check(DiarkisPacket.ParseUdp(Convert.FromHexString("01000004")).Payload.Length == 0, "ACK 可以没有载荷");
        Reject(() => DiarkisPacket.ParseUdp(new byte[3]), "拒绝截断 UDP 头");
        RejectArgument(() => DiarkisPacket.CreateUdp(3, 0x1000000, []), "越界序号不得静默截断");
        RejectArgument(() => DiarkisPacket.CreateRequest(2, 3060, new byte[0x1000000]), "越界长度不得覆盖版本位");

        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var macKey = Enumerable.Range(32, 32).Select(i => (byte)i).ToArray();
        // 空明文的密文沿用独立 Python cryptography/HMAC 向量，SID 为合成值。
        var encrypted = Convert.FromHexString("00000000EEDC6BF8345930F2C0AC86647FA7085F1630D2A43B5CD1492CA8285AEAFE9D1907FEEF74E1D5036E900EEE118E949293");
        var request = DiarkisPacket.CreateEncryptedRequest(2, 3060, [], [0x10, 0x20], key, iv, macKey);
        check(request.SequenceEqual(Convert.FromHexString("FEBEDEEF020000360BF41020").Concat(encrypted)),
            "加密请求的 SID 在密文前，头部长度包含 SID 和加密封装");
        var encryptedResponse = DiarkisPacket.ParseResponse(Convert.FromHexString("FEBEDEEF020000340BF400").Concat(encrypted).ToArray());
        check(DiarkisEncryption.Decrypt(encryptedResponse.Payload, key, iv, macKey).Length == 0,
            "响应载荷直接解密，不错误剥离 SID");

        void Reject(Action action, string message)
        {
            var rejected = false;
            try { action(); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, message);
        }

        void RejectArgument(Action action, string message)
        {
            var rejected = false;
            try { action(); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, message);
        }
    }
}
