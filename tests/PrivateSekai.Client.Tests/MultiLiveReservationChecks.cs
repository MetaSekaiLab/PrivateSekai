extern alias game;

using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json.Nodes;
using game::Sekai;
using game::Sekai.MultiLive;
using MessagePack;
using PrivateSekai.Client.Realtime;
using PrivateSekai.Protocol;

internal static class MultiLiveReservationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var vectors = new (byte[] Actual, string Expected)[]
        {
            (SyncPropertyEncoding.Byte(255), "0100000001FF"),
            (SyncPropertyEncoding.Int32(0), "020000000400000000"),
            (SyncPropertyEncoding.Int32(-1), "0200000004FFFFFFFF"),
            (SyncPropertyEncoding.Int32(int.MinValue), "020000000480000000"),
            (SyncPropertyEncoding.Int32(int.MaxValue), "02000000047FFFFFFF"),
            (SyncPropertyEncoding.Int64(long.MinValue), "03000000088000000000000000"),
            (SyncPropertyEncoding.Int64(long.MaxValue), "03000000087FFFFFFFFFFFFFFF"),
            (SyncPropertyEncoding.String(""), "0400000000"),
            (SyncPropertyEncoding.String("房😀"), "0400000007E688BFF09F9880"),
            (SyncPropertyEncoding.Float(-2.5f), "0500000004C0200000"),
            (SyncPropertyEncoding.Float(float.PositiveInfinity), "05000000047F800000"),
            (SyncPropertyEncoding.Float(BitConverter.Int32BitsToSingle(int.MinValue)), "050000000480000000"),
            (SyncPropertyEncoding.Boolean(false), "060000000100"),
            (SyncPropertyEncoding.Boolean(true), "060000000101"),
            (SyncPropertyEncoding.Object(new byte[] { 0xC0 }), "0700000001C0"),
            (SyncPropertyEncoding.Object(new byte[] { 0x81, 0xA1, 0x78, 0x01 }), "070000000481A17801")
        };
        foreach (var (actual, expected) in vectors)
            check(actual.SequenceEqual(Convert.FromHexString(expected)), "同步属性固定向量：类型、大端长度和原始内容");

        var normal = Parse(MultiLiveReservation.CreateRequest(LiveRuleType.normal, 3600, default, false, null, null));
        check(normal.multiLiveRuleType == "normal" && normal.roomTTL == 3600 && normal.roomProperty.isRSend == 1,
            "预留请求保留规则字符串、配置 TTL 和可靠标记");
        var values = normal.roomProperty.values;
        check(values.Keys.Order().SequenceEqual(new[] { 1, 2, 3, 5, 6, 8, 10, 12, 13, 14, 15, 16, 17, 18 }),
            "预留请求仅包含创建时的十四项属性，不发送所选歌曲、玩家或公开状态");
        check(ReadInt(values[1]) == 0 && ReadInt(values[2]) == 1 && ReadInt(values[3]) == 2
            && ReadInt(values[5]) == 0 && ReadInt(values[6]) == 0 && ReadInt(values[10]) == 0
            && ReadInt(values[14]) == 0 && ReadInt(values[15]) == 0 && ReadInt(values[16]) == 0,
            "预留状态使用 Reserve、零房号及零大厅，不套用公开匹配状态");
        check(values[8].SequenceEqual(Convert.FromHexString("010000000100"))
            && values[12].SequenceEqual(Convert.FromHexString("0400000000"))
            && values[13].SequenceEqual(Convert.FromHexString("0400000000"))
            && values[18].SequenceEqual(Convert.FromHexString("060000000100")),
            "预留的 byte、空字符串和 bool 保持各自类型");
        var defaults = Settings(values[17]);
        check(defaults.Count == 5 && defaults.ContainsKey("MusicDifficultyTypes")
            && defaults["MusicDifficultyTypes"] == null
            && defaults["ScoreCalculateType"]!.GetValue<int>() == 0
            && defaults["MusicSelectionType"]!.GetValue<int>() == 0
            && !defaults["IsDisplayPlayerInfo"]!.GetValue<bool>()
            && defaults["ScoreSelectType"]!.GetValue<int>() == 0,
            "默认设置固定五个字段，难度数组明确为 nil 而非省略或空数组");

        var settings = new CustomRoomSettingData
        {
            scoreCalculateType = ScoreCalculateType.competitive,
            musicSelectionType = MusicSelectionType.random,
            musicDifficultyTypes = [MusicDifficulty.easy, MusicDifficulty.expert],
            isDisplayPlayerInfo = true,
            scoreSelectType = ScoreSelectType.OwnerOnly
        };
        var custom = Parse(MultiLiveReservation.CreateRequest(LiveRuleType.custom, 17, settings, true, 100, 75000));
        var configured = Settings(custom.roomProperty.values[17]);
        check(custom.multiLiveRuleType == "custom" && custom.roomTTL == 17
            && ReadInt(custom.roomProperty.values[14]) == 1
            && ReadInt(custom.roomProperty.values[15]) == 75000 && ReadInt(custom.roomProperty.values[16]) == 100,
            "不同规则、TTL 和战力范围从参数编码，不绑定某个官方样本");
        check(configured["ScoreCalculateType"]!.GetValue<int>() == 1
            && configured["MusicSelectionType"]!.GetValue<int>() == 2
            && configured["MusicDifficultyTypes"]!.AsArray().Select(n => n!.GetValue<int>()).SequenceEqual(new[] { 1, 4 })
            && configured["IsDisplayPlayerInfo"]!.GetValue<bool>()
            && configured["ScoreSelectType"]!.GetValue<int>() == 1
            && custom.roomProperty.values[18].SequenceEqual(Convert.FromHexString("060000000101")),
            "自定义设置保留枚举、多个难度及显示开关");
        settings.musicDifficultyTypes = [];
        var empty = Parse(MultiLiveReservation.CreateRequest(LiveRuleType.custom, 23, settings, false, 0, 0));
        check(Settings(empty.roomProperty.values[17])["MusicDifficultyTypes"]!.AsArray().Count == 0,
            "明确空难度数组与默认 nil 区分");

        CreateMultiLivePrivateRoomData Parse(byte[] packed)
        {
            // 独立读取外层：属性 ID 必须是整数，属性值必须是二进制。
            var reader = new MessagePackReader(new ReadOnlySequence<byte>(packed));
            check(reader.ReadMapHeader() == 3, "预留请求为三个具名字段的 map");
            var names = new HashSet<string>();
            for (var i = 0; i < 3; i++)
            {
                var name = reader.ReadString()!;
                names.Add(name);
                if (name != "RoomProperty") { reader.Skip(); continue; }
                check(reader.ReadMapHeader() == 2, "动态属性包含 R 和 Values");
                var propertyNames = new HashSet<string>();
                for (var j = 0; j < 2; j++)
                {
                    var propertyName = reader.ReadString()!;
                    propertyNames.Add(propertyName);
                    if (propertyName == "R") { check(reader.ReadByte() == 1, "动态属性 R 为数值 1"); continue; }
                    var count = reader.ReadMapHeader();
                    for (var k = 0; k < count; k++)
                    {
                        var id = reader.ReadInt32();
                        var bytes = reader.ReadBytes()!.Value.ToArray();
                        check(id > 0 && bytes.Length >= 5
                            && BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(1)) == bytes.Length - 5,
                            "属性使用整数 ID、bin 类型及准确的字节长度");
                    }
                }
                check(propertyNames.SetEquals(["R", "Values"]), "动态属性使用 dump 声明的字段名");
            }
            check(reader.End && names.SetEquals(["MultiLiveRuleType", "RoomTTL", "RoomProperty"]),
                "预留请求字段名和边界与 dump 契约一致");
            return DumpSerializer.Deserialize<CreateMultiLivePrivateRoomData>(packed);
        }
    }

    private static int ReadInt(byte[] value)
    {
        if (value.Length != 9 || value[0] != 2 || BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(1)) != 4)
            throw new InvalidDataException("预期 Int32 同步属性。");
        return BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(5));
    }

    private static JsonObject Settings(byte[] value)
    {
        if (value[0] != 7 || BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(1)) != value.Length - 5)
            throw new InvalidDataException("预期 Object 同步属性。");
        return JsonNode.Parse(MessagePackSerializer.ConvertToJson(value.AsMemory(5)))!.AsObject();
    }
}
