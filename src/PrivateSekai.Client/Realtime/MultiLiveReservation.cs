extern alias game;

using System.Buffers;
using System.Globalization;
using System.Text.Json.Nodes;
using game::CP.Realtime;
using game::Sekai;
using game::Sekai.MultiLive;
using MessagePack;
using PrivateSekai.Protocol;

namespace PrivateSekai.Client.Realtime;

public static class MultiLiveReservation
{
    public const byte Version = 2;
    public const ushort Command = 3060;

    public static int ReadRoomNumber(JsonObject response, string roomId)
    {
        var number = response["roomNo"]?.GetValue<int>() ?? 0;
        if (number <= 0 || response["roomId"]?.GetValue<string>() != roomId
            || response["privateRoomType"]?.GetValue<string>() != "multi_live"
            || response["liveRuleType"]?.GetValue<string>() != "normal")
            throw new InvalidDataException("普通多人房间响应缺少有效房号或与当前预留不一致。");
        return number;
    }

    public static int ReadRoomTtl(JsonArray clientConfigs, LiveRuleType rule)
    {
        var id = rule switch
        {
            LiveRuleType.normal => 181,
            LiveRuleType.custom => 182,
            _ => throw new InvalidDataException("未识别的房间规则。")
        };
        var row = clientConfigs.Single(c => c?["id"]?.GetValue<int>() == id)!;
        if (row["type"]?.GetValue<string>() != "Int"
            || !int.TryParse(row["value"]?.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ttl)
            || ttl <= 0)
            throw new InvalidDataException("房间 TTL 配置无效。");
        return ttl;
    }

    public static CreateMultiLivePrivateRoomResponse ParseResponse(DiarkisResponse response)
    {
        if (response.Version != Version || response.Command != Command || response.Status != 1)
            throw new InvalidDataException("房间预留未返回成功响应。");
        var result = DumpSerializer.Deserialize<CreateMultiLivePrivateRoomResponse>(response.Payload);
        if (string.IsNullOrEmpty(result.roomId))
            throw new InvalidDataException("房间预留响应缺少 RoomID。");
        return result;
    }

    // TTL 由调用方按 liveRuleType 从对应客户端配置取得。
    public static byte[] CreateRequest(LiveRuleType liveRuleType, int roomTtl,
        CustomRoomSettingData settings, bool showsRoomId, int? lowerPower, int? upperPower)
    {
        var request = new CreateMultiLivePrivateRoomData
        {
            multiLiveRuleType = liveRuleType.ToString(),
            roomTTL = roomTtl,
            roomProperty = new DynamicPropertyPayload
            {
                isRSend = 1,
                values = new Dictionary<int, byte[]>
                {
                    [1] = SyncPropertyEncoding.Int32(0), // MESSAGE
                    [2] = SyncPropertyEncoding.Int32(1), // STEP
                    [3] = SyncPropertyEncoding.Int32(2), // ATYPE: Reserve
                    [5] = SyncPropertyEncoding.Int32(0), // MASTER_LOBBY_ID: 预留时为零
                    [6] = SyncPropertyEncoding.Int32(0), // RECRUIT_TOTAL_POWER
                    [8] = SyncPropertyEncoding.Byte(0), // MATCH_SCALEUP_FINISH
                    [10] = SyncPropertyEncoding.Int32(0), // ROOM_NUMBER
                    [12] = SyncPropertyEncoding.String(""), // LIVE_ID
                    [13] = SyncPropertyEncoding.String(""), // RANDOM_SEED
                    [14] = SyncPropertyEncoding.Int32((int)liveRuleType), // LIVE_RULE_TYPE
                    [15] = SyncPropertyEncoding.Int32(upperPower.GetValueOrDefault()),
                    [16] = SyncPropertyEncoding.Int32(lowerPower.GetValueOrDefault()),
                    [17] = SyncPropertyEncoding.Object(SerializeSettings(settings)),
                    [18] = SyncPropertyEncoding.Boolean(showsRoomId)
                }
            }
        };
        return DumpSerializer.Serialize(request);
    }

    private static byte[] SerializeSettings(CustomRoomSettingData settings)
    {
        // 原 formatter 固定写出全部字段，包括为 nil 的难度数组。
        var members = DumpContract.For(typeof(CustomRoomSettingData)).Members;
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(members.Count);
        foreach (var member in members)
        {
            writer.Write((string)member.Key);
            MessagePackSerializer.Serialize(member.Type, ref writer, member.Get(settings), DumpSerializer.Options);
        }
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }
}
