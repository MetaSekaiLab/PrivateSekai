extern alias game;

using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using game::CP.Realtime;
using game::Sekai.MultiLive;
using MessagePack;
using PrivateSekai.Client.Realtime;
using PrivateSekai.Protocol;
using MultiLiveRoom = PrivateSekai.Client.Realtime.MultiLiveRoom;

internal static class MultiLiveRoomChecks
{
    public static void Run(Action<bool, string> check)
    {
        var id = new string('r', 52);
        const string userId = "1234567890";
        const string name = "合成玩家😀";
        const int number = 48291;
        var info = new RoomUserBasicInfo
        {
            CardId = 41, CardLevel = 23, CardSkillLv = 2, CardMasterRank = 1,
            SubCardIds = [42, 43, 44, 45], SubCardSkillLv = [1, 2, 3, 4], SubCardImages = ["a", "b", "c", "d"],
            TotalPowerIncludeBuff = 12345, DefaultImage = "original", IsTraining = true,
            MainHonor = new() { Type = 1, Id = 71, Level = 2, HonorMissionProgress = 19 },
            SubHonors = [new() { Type = 1 }, new() { Type = 2, Id = 72 }],
            HairCostumeId = 81, BodyCostumeId = 82, AccessoryCostumeId = 83,
            MemberCharacterRank = [new() { characterId = 1, characterRank = 7 }], UserPlayerFrameId = 91,
            UserPlayerFramePartsLayout = []
        };
        var request = MultiLiveRoom.CreateRequest(id, number, name, info);
        var json = JsonNode.Parse(MessagePackSerializer.ConvertToJson(request))!.AsObject();
        check(json.Count == 6 && json.ContainsKey("TotalPowerUpperLimit") && json["TotalPowerUpperLimit"] == null
            && json.ContainsKey("TotalPowerLowerLimit") && json["TotalPowerLowerLimit"] == null,
            "私人创建固定六字段，上下限明确为 nil");
        var data = DumpSerializer.Deserialize<MultiLiveUnLockJoinData>(request);
        check(data.roomId == id && data.privateRoomNumber == number && data.totalPower == 12345
            && data.playerProperty.isRSend == 1 && data.playerProperty.values.Keys.Order().SequenceEqual(new[] { 1, 2, 5, 7 }),
            "3070 创建携带预留房间、分配房号、实际战力及四项可靠玩家属性");
        var values = data.playerProperty.values;
        var packedInfo = values[2].AsMemory(5);
        var array = JsonNode.Parse(MessagePackSerializer.ConvertToJson(packedInfo))!.AsArray();
        check(array.Count == 20 && array[0]!.GetValue<int>() == 41 && array[7]!.GetValue<int>() == 12345
            && array[19]!.AsArray().Count == 0, "基本资料使用原始二十项数组契约，含玩家框布局");
        check(SyncPropertyEncoding.ReadString(values[1]) == name && SyncPropertyEncoding.ReadInt32(values[5]) == 1
            && SyncPropertyEncoding.ReadString(values[7]) == "normal", "玩家名按 UTF8，加入路径和难度使用私房初始值");
        var sync = new MultiLiveRoomSyncData
        {
            roomId = id, privateRoomNumber = number, userId = userId, ownerId = userId, roomCreateTime = 123,
            index = 0, playerProperty = data.playerProperty, players = [], roomProperty = new() { isRSend = 1, values = [] }
        };
        var response = Response(sync);
        check(MultiLiveRoom.ParseCreated(response, id, number, userId, name, info).index == 0,
            "3000 同步验证房间、自己、房主、索引和完整玩家资料后确认入房");
        check(MultiLiveRoom.ParseCreated(response with { Command = 3001 }, id, number, userId, name, info).index == 0,
            "预留房间创建也可返回3001，仍完整校验入房资料");
        Reject(() => Parse(response with { Command = 3070 }));
        Reject(() => Parse(response with { Status = 255 }));
        Reject(() => Parse(response with { Version = 1 }));
        sync.ownerId = "other";
        Reject(() => Parse(Response(sync)));
        sync.ownerId = userId;
        sync.privateRoomNumber++;
        Reject(() => Parse(Response(sync)));
        sync.privateRoomNumber = number;
        sync.index = 7;
        check(MultiLiveRoom.ParseCreated(Response(sync), id, number, userId, name, info).index == 7,
            "服务端分配的玩家索引保留原值，不套用本地编队槽位范围");
        sync.index = 0;
        values[2] = SyncPropertyEncoding.Object(DumpSerializer.Serialize(new RoomUserBasicInfo { CardId = 99 }));
        Reject(() => Parse(Response(sync)));

        var property = MultiLiveRoom.RoomNumberRequest(number);
        var decoded = DumpSerializer.Deserialize<DynamicPropertyPayload>(property);
        check(decoded.isRSend == 1 && decoded.values.Count == 1 && SyncPropertyEncoding.ReadInt64(decoded.values[10]) == number,
            "创建完成后房号使用 Int64 同步，不沿用预留时的 Int32 零值");
        var pushed = new DiarkisResponse(2, 10010, 255, Frame(property), 0);
        check(MultiLiveRoom.ConfirmsRoomNumber(pushed, number)
            && !MultiLiveRoom.ConfirmsRoomNumber(pushed, number + 1)
            && !MultiLiveRoom.ConfirmsRoomNumber(pushed with { Status = 1, Payload = [] }, number),
            "只有实际推送中房号匹配才能确认，空请求确认不算同步成功");
        var unrelated = DumpSerializer.Serialize(new DynamicPropertyPayload { isRSend = 1, values = new() { [1] = SyncPropertyEncoding.Int32(0) } });
        check(MultiLiveRoom.ConfirmsRoomNumber(pushed with { Payload = [.. Frame(unrelated), .. Frame(property)] }, number),
            "属性推送可以包含多帧，逐帧查找房号");
        check(!MultiLiveRoom.ConfirmsRoomNumber(pushed with { Payload = [.. Frame(property), .. Frame(MultiLiveRoom.RoomNumberRequest(number + 1))] }, number),
            "同包后续房号更新覆盖先前值，不能只因早期帧匹配就确认");
        Reject(() => MultiLiveRoom.ConfirmsRoomNumber(pushed with { Payload = [0, 0, 0] }, number));
        Reject(() => MultiLiveRoom.ConfirmsRoomNumber(pushed with { Payload = [0x7F, 0xFF, 0xFF, 0xFF, 0] }, number));
        Reject(() => MultiLiveRoom.ConfirmsRoomNumber(pushed with { Payload = [.. Frame(property), 0] }, number));
        check(Encoding.UTF8.GetString(MultiLiveRoom.LeaveRequest(id, userId)) == id + id + userId,
            "退出按 CP 与 SDK 两层拼接房间 ID，再带本玩家 ID");
        foreach (var invalid in new byte[][] { [], [3, 0, 0, 0, 8, 0], [2, 0, 0, 0, 1, 0] })
            Reject(() => SyncPropertyEncoding.ReadInt32(invalid));
        check(SyncPropertyEncoding.ReadInt64(SyncPropertyEncoding.Int64(long.MinValue)) == long.MinValue
            && SyncPropertyEncoding.ReadString(SyncPropertyEncoding.String(name)) == name, "同步属性读取校验类型与长度并保留数值及 Unicode");

        void Parse(DiarkisResponse value) => MultiLiveRoom.ParseCreated(value, id, number, userId, name, info);
        static DiarkisResponse Response(MultiLiveRoomSyncData value) => new(2, 3000, 1, DumpSerializer.Serialize(value), 0);
        void Reject(Action action)
        {
            var rejected = false;
            try { action(); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "错误同步、截断或错误类型不能被当作已完成");
        }
        static byte[] Frame(byte[] value)
        {
            var result = new byte[4 + value.Length];
            BinaryPrimitives.WriteInt32BigEndian(result, value.Length);
            value.CopyTo(result, 4);
            return result;
        }
    }
}
