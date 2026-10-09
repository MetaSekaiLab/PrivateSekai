using PrivateSekai.Client.Realtime;

internal static class DiarkisSplitPacketChecks
{
    public static void Run(Action<bool, string> check)
    {
        var fixedParts = new[]
        {
            Convert.FromHexString("FFFEFDFC1234000000031020"),
            Convert.FromHexString("FFFEFDFC1234000100033040"),
            Convert.FromHexString("FFFEFDFC12340002000350")
        };
        var created = DiarkisSplitPacket.Create(0x1234, [0x10, 0x20, 0x30, 0x40, 0x50], 2);
        check(created.Count == 3 && created.Zip(fixedParts).All(pair => pair.First.SequenceEqual(pair.Second)),
            "分片编码匹配固定样本，末片只包含剩余字节");
        var single = DiarkisSplitPacket.Create(-1, [0xAA], 1300);
        check(single.Count == 1 && single[0].SequenceEqual(Convert.FromHexString("FFFEFDFCFFFF00000001AA")),
            "单片仍有分片头，组 ID 按有符号 16 位保留");
        check(DiarkisSplitPacket.Create(0, [], 1300).Count == 0, "空输入不生成空分片");
        foreach (var length in new[] { 1300, 1301, 2600, 2601 })
        {
            var payload = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
            var packets = DiarkisSplitPacket.Create(9, payload, 1300);
            var collector = new DiarkisSplitPacket(packets[^1], false);
            foreach (var packet in packets.Reverse()) collector.Add(packet);
            check(collector.IsComplete && collector.GetBytes().SequenceEqual(payload),
                "完整块、边界及多片逆序输入均可按索引重组");
        }
        foreach (var order in new[] { new[] { 0, 1, 2 }, [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0] })
        {
            var collector = new DiarkisSplitPacket(fixedParts[order[0]], false);
            check(collector.Id == 0x1234 && collector.Count == 3 && !collector.IsComplete && collector.GetBytes().Length == 0,
                "首片可以为任意索引，未收齐不交付部分消息");
            collector.Add(fixedParts[order[1]]);
            check(!collector.Add(fixedParts[order[0]]) && !collector.IsComplete, "重复片不增加完成计数");
            collector.Add(fixedParts[order[2]]);
            check(collector.IsComplete && collector.GetBytes().SequenceEqual(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 }),
                "三片全部到达顺序均按片索引重组");
        }
        var modern = new DiarkisSplitPacket(fixedParts[1], false);
        var legacy = new DiarkisSplitPacket(fixedParts[1], true);
        foreach (var index in new[] { 0, 2 })
        {
            modern.Add(fixedParts[index]);
            legacy.Add(fixedParts[index]);
        }
        check(modern.GetBytes().SequenceEqual(Convert.FromHexString("1020304050"))
            && legacy.GetBytes().SequenceEqual(Convert.FromHexString("1020503040")),
            "旧版 Insert 在 1、0、2 到达时保留与新版不同的静态行为");
        var orderedLegacy = new DiarkisSplitPacket(fixedParts[0], true);
        orderedLegacy.Add(fixedParts[1]);
        orderedLegacy.Add(fixedParts[2]);
        check(orderedLegacy.GetBytes().SequenceEqual(Convert.FromHexString("1020304050")), "旧版顺序到达仍按原顺序拼接");
        var wrongGroup = fixedParts[0].ToArray();
        wrongGroup[5]++;
        check(!modern.Add(wrongGroup), "其他组的片不能混入当前结果");
        var changedDuplicate = fixedParts[0].ToArray();
        changedDuplicate[^1] = 0xFF;
        check(!modern.Add(changedDuplicate) && modern.GetBytes().SequenceEqual(Convert.FromHexString("1020304050")),
            "重复片即使内容不同也不覆盖首次载荷");
        var changedCount = fixedParts[0].ToArray();
        changedCount[9] = 4;
        Reject(() => modern.Add(changedCount), "本地拒绝同组总数变化");
        foreach (var invalid in new[] { "FFFEFDFC123400000003", "FFFEFDFC123400000000AA",
            "FFFEFDFC1234FFFF0003AA", "FFFEFDFC123400030003AA", "FFFEFDFC123400008000AA", "FEFEFDFC123400000003AA" })
            Reject(() => new DiarkisSplitPacket(Convert.FromHexString(invalid), false), "本地拒绝截断、空载荷、错误标识或非法片索引");
        RejectArgument(() => DiarkisSplitPacket.Create(0, [1], 0), "拒绝非正分片大小");
        RejectArgument(() => DiarkisSplitPacket.Create(0, new byte[32768], 1), "拒绝分片计数溢出");

        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var macKey = Enumerable.Range(32, 32).Select(i => (byte)i).ToArray();
        var sid = Enumerable.Range(64, 16).Select(i => (byte)i).ToArray();
        var small = DiarkisPacket.CreateReliableRequests(2, 3060, new byte[1231], sid, key, iv, macKey, 7);
        check(small.Count == 1 && small[0].Length == 1294 && !DiarkisSplitPacket.IsSplitPacket(small[0].AsSpan(26)),
            "可靠发送按加密后的完整业务包长度判断是否分片");
        var source = Enumerable.Range(0, 1232).Select(i => (byte)i).ToArray();
        var large = DiarkisPacket.CreateReliableRequests(2, 3060, source, sid, key, iv, macKey, 7);
        check(large.Count == 2 && large[0].Length == 1336 && large[1].Length == 46
            && large.All(packet => packet.AsSpan(10, 16).SequenceEqual(sid)),
            "大包先整体加密再分片，各片外层重新加入 SID 和业务头");
        var reassembled = new DiarkisSplitPacket(large[0].AsSpan(26), false);
        reassembled.Add(large[1].AsSpan(26));
        var whole = reassembled.GetBytes();
        check(whole.SequenceEqual(DiarkisPacket.CreateEncryptedRequest(2, 3060, source, sid, key, iv, macKey))
            && DiarkisEncryption.Decrypt(whole.AsSpan(26), key, iv, macKey).SequenceEqual(source),
            "分片重组恢复完整内部请求和明文，分片不是分别加密");

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
