using System.Buffers.Binary;
using System.Text;
using PrivateSekai.Client.Realtime;

internal static class DiarkisEchoChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(DiarkisEcho.CreateRequest(1, ["a:1", "b:2"])
            .SequenceEqual(Convert.FromHexString("000000000000F03F00000003613A3100000003623A32")),
            "Echo 请求时间戳小端 double，地址为大端长度前缀字符串列表");
        var legacy = new byte[] { 2 }.Concat(Convert.FromHexString("000000000000F03F"))
            .Concat(Encoding.UTF8.GetBytes("127.0.0.1:9")).ToArray();
        var parsedLegacy = DiarkisEcho.Parse(legacy);
        check(parsedLegacy.IsOffline && parsedLegacy.Timestamp == 1 && parsedLegacy.Address == "127.0.0.1:9"
            && parsedLegacy.Notifications.Count == 0, "兼容无附加数据的旧式 Echo，保留离线标记");
        var modern = new byte[] { 1 }.Concat(Convert.FromHexString("000000000000F03F"))
            .Concat(Encoding.UTF8.GetBytes("a:1"))
            .Concat(Convert.FromHexString("0007021234AABB0005000190000C")).ToArray();
        var parsed = DiarkisEcho.Parse(modern);
        check(!parsed.IsOffline && parsed.Address == "a:1" && parsed.Timestamp == 1 && parsed.Notifications.Count == 2,
            "Echo 按末尾边界区分地址与多个通知");
        check(parsed.Notifications[0].Version == 2 && parsed.Notifications[0].Command == 0x1234
            && parsed.Notifications[0].IsPush && parsed.Notifications[0].Payload.SequenceEqual(new byte[] { 0xAA, 0xBB })
            && parsed.Notifications[1].Command == 400 && parsed.Notifications[1].Payload.Length == 0,
            "通知保留版本、命令与空载荷，不只支持单个通知编号");
        foreach (var length in new[] { 0, 1, 8 }) Reject(new byte[length]);
        var badBoundary = modern.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(badBoundary.AsSpan(badBoundary.Length - 2), 8);
        Reject(badBoundary);
        var zeroNotification = modern.ToArray();
        zeroNotification[13] = 0;
        Reject(zeroNotification);
        var oversizedNotification = modern.ToArray();
        oversizedNotification[12] = 255;
        Reject(oversizedNotification);

        void Reject(byte[] payload)
        {
            var failed = false;
            try { DiarkisEcho.Parse(payload); }
            catch (InvalidDataException) { failed = true; }
            check(failed, "本地拒绝截断 Echo、越界地址或非法通知长度");
        }
    }
}
