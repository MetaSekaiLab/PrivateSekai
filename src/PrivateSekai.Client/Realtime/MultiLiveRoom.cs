extern alias game;

using System.Buffers.Binary;
using System.Text;
using game::CP.Realtime;
using game::Sekai.MultiLive;
using PrivateSekai.Protocol;

namespace PrivateSekai.Client.Realtime;

public static class MultiLiveRoom
{
    public const ushort CreateCommand = 3070;
    public const ushort CreatedCommand = 3000;
    public const ushort JoinedCommand = 3001;
    public const ushort JoinCommand = 3080;
    public const ushort PropertyCommand = 10010;
    public const ushort LeaveCommand = 102;
    public const ushort UserIndexCommand = 1021;

    public static byte[] UserIndexRequest(string userId) => DumpSerializer.Serialize(new UserIdPayload { userId = userId });

    public static int ParseUserIndex(DiarkisResponse response, string userId)
    {
        if (response.Version != MultiLiveReservation.Version || response.Command != UserIndexCommand || response.Status != 1)
            throw new InvalidDataException("成员索引查询未成功。");
        var result = DumpSerializer.Deserialize<UserIndexPayload>(response.Payload);
        if (result.userId != userId || result.index == 0) throw new InvalidDataException("成员索引回包缺少有效索引或玩家不一致。");
        return result.index;
    }

    public static byte[] RoomNumberRequest(int roomNumber) => DumpSerializer.Serialize(new DynamicPropertyPayload
    {
        isRSend = 1, values = new() { [10] = SyncPropertyEncoding.Int64(roomNumber) }
    });

    public static bool ConfirmsRoomNumber(DiarkisResponse response, int roomNumber)
    {
        if (response.Version != MultiLiveReservation.Version || response.Command != PropertyCommand || !response.IsPush)
            return false;
        var matches = false;
        foreach (var frame in ReadFrames(response.Payload))
        {
            var property = DumpSerializer.Deserialize<DynamicPropertyPayload>(frame);
            if (property.values?.TryGetValue(10, out var value) == true)
                matches = value.Length > 0 && value[0] == 3 && SyncPropertyEncoding.ReadInt64(value) == roomNumber;
        }
        return matches;
    }

    public static byte[] JoinRequest(string roomId, string nickname, RoomUserBasicInfo info)
    {
        if (roomId.Length != 52 || info.TotalPowerIncludeBuff < 0) throw new InvalidDataException("加入房间标识或玩家资料无效。");
        return DumpSerializer.Serialize(new MultiLiveDirectJoinData
        {
            roomId = roomId, totalPower = info.TotalPowerIncludeBuff, playerProperty = PlayerProperty(nickname, info)
        });
    }

    public static DynamicPropertyPayload PlayerProperty(string nickname, RoomUserBasicInfo info) => new()
    {
        isRSend = 1,
        values = new()
        {
            [1] = SyncPropertyEncoding.String(nickname), [2] = SyncPropertyEncoding.Object(DumpSerializer.Serialize(info)),
            [5] = SyncPropertyEncoding.Int32(1), [7] = SyncPropertyEncoding.String("normal")
        }
    };

    public static RoomSyncPlayer[] ReadMemberJoins(DiarkisResponse response)
    {
        if (response.Version != MultiLiveReservation.Version || response.Command != JoinedCommand || !response.IsPush)
            throw new InvalidDataException("未收到成员加入推送。");
        return ReadFrames(response.Payload).Select(DumpSerializer.Deserialize<RoomSyncPlayer>).ToArray();
    }

    public static string[] ReadMemberLeaves(DiarkisResponse response, string roomId)
    {
        if (response.Version != 1 || response.Command != LeaveCommand || !response.IsPush)
            throw new InvalidDataException("未收到成员退出推送。");
        return ReadFrames(response.Payload).Select(frame =>
        {
            if (frame.Length <= 52 || Encoding.UTF8.GetString(frame.Span[..52]) != roomId)
                throw new InvalidDataException("成员退出推送的房间或玩家标识无效。");
            return Encoding.UTF8.GetString(frame.Span[52..]);
        }).ToArray();
    }

    internal static List<ReadOnlyMemory<byte>> ReadFrames(ReadOnlyMemory<byte> payload)
    {
        var frames = new List<ReadOnlyMemory<byte>>();
        while (!payload.IsEmpty)
        {
            if (payload.Length < 5) throw new InvalidDataException("房间推送帧头不完整。");
            var length = BinaryPrimitives.ReadInt32BigEndian(payload.Span);
            if (length <= 0 || length > payload.Length - 4) throw new InvalidDataException("房间推送帧长度无效。");
            frames.Add(payload.Slice(4, length));
            payload = payload[(4 + length)..];
        }
        return frames;
    }

    public static byte[] LeaveRequest(string roomId, string userId)
    {
        if (roomId.Length != 52 || string.IsNullOrEmpty(userId)) throw new InvalidDataException("退出房间标识无效。");
        return Encoding.UTF8.GetBytes(roomId + roomId + userId);
    }

    public static byte[] CreateRequest(string roomId, int roomNumber, string nickname, RoomUserBasicInfo info)
    {
        if (roomId.Length != 52 || roomNumber <= 0 || info.TotalPowerIncludeBuff < 0)
            throw new InvalidDataException("私人房间或玩家资料无效。");
        return MultiLiveReservation.SerializeFixedMap(new MultiLiveUnLockJoinData
        {
            roomId = roomId, privateRoomNumber = roomNumber, totalPower = info.TotalPowerIncludeBuff,
            totalPowerUpperLimit = null, totalPowerLowerLimit = null,
            playerProperty = PlayerProperty(nickname, info)
        });
    }

    public static MultiLiveRoomSyncData ParseCreated(DiarkisResponse response, string roomId, int roomNumber,
        string userId, string nickname, RoomUserBasicInfo expected, string? ownerId = null)
    {
        if (response.Version != MultiLiveReservation.Version || response.Command is not (CreatedCommand or JoinedCommand) || response.Status != 1)
            throw new InvalidDataException("创建房间尚未收到成功入房同步。");
        var sync = DumpSerializer.Deserialize<MultiLiveRoomSyncData>(response.Payload);
        if (sync.roomId != roomId || sync.privateRoomNumber != roomNumber || sync.userId != userId || sync.ownerId != (ownerId ?? userId)
            || sync.roomCreateTime == 0 || sync.releaseRoom)
            throw new InvalidDataException("入房同步与创建的私人房间或本玩家不一致。");
        RequirePlayer(sync.playerProperty, PlayerProperty(nickname, expected));
        return sync;
    }

    public static void RequirePlayer(DynamicPropertyPayload actual, DynamicPropertyPayload expected)
    {
        var values = actual.values ?? throw new InvalidDataException("玩家同步缺少属性。");
        if (new[] { 1, 2, 5, 7 }.Any(key => !values.TryGetValue(key, out var value) || value == null))
            throw new InvalidDataException("玩家同步属性不完整。");
        if (SyncPropertyEncoding.ReadString(values[1]) != SyncPropertyEncoding.ReadString(expected.values[1])
            || SyncPropertyEncoding.ReadInt32(values[5]) != SyncPropertyEncoding.ReadInt32(expected.values[5])
            || SyncPropertyEncoding.ReadString(values[7]) != SyncPropertyEncoding.ReadString(expected.values[7])
            || !DumpSerializer.Serialize(SyncPropertyEncoding.ReadObject<RoomUserBasicInfo>(values[2]))
                .AsSpan().SequenceEqual(DumpSerializer.Serialize(SyncPropertyEncoding.ReadObject<RoomUserBasicInfo>(expected.values[2]))))
            throw new InvalidDataException("玩家同步资料与预期不一致。");
    }
}
