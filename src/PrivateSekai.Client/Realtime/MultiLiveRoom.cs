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
    public const ushort PropertyCommand = 10010;
    public const ushort LeaveCommand = 102;

    public static byte[] RoomNumberRequest(int roomNumber) => DumpSerializer.Serialize(new DynamicPropertyPayload
    {
        isRSend = 1, values = new() { [10] = SyncPropertyEncoding.Int64(roomNumber) }
    });

    public static bool ConfirmsRoomNumber(DiarkisResponse response, int roomNumber)
    {
        if (response.Version != MultiLiveReservation.Version || response.Command != PropertyCommand || !response.IsPush)
            return false;
        var payload = response.Payload.AsMemory();
        var matches = false;
        while (!payload.IsEmpty)
        {
            if (payload.Length < 5) throw new InvalidDataException("房间属性推送帧头不完整。");
            var length = BinaryPrimitives.ReadInt32BigEndian(payload.Span);
            if (length <= 0 || length > payload.Length - 4) throw new InvalidDataException("房间属性推送帧长度无效。");
            var property = DumpSerializer.Deserialize<DynamicPropertyPayload>(payload.Slice(4, length));
            if (property.values?.TryGetValue(10, out var value) == true)
                matches = value.Length > 0 && value[0] == 3 && SyncPropertyEncoding.ReadInt64(value) == roomNumber;
            payload = payload[(4 + length)..];
        }
        return matches;
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
            playerProperty = new DynamicPropertyPayload
            {
                isRSend = 1,
                values = new()
                {
                    [1] = SyncPropertyEncoding.String(nickname),
                    [2] = SyncPropertyEncoding.Object(DumpSerializer.Serialize(info)),
                    [5] = SyncPropertyEncoding.Int32(1),
                    [7] = SyncPropertyEncoding.String("normal")
                }
            }
        });
    }

    public static MultiLiveRoomSyncData ParseCreated(DiarkisResponse response, string roomId, int roomNumber,
        string userId, string nickname, RoomUserBasicInfo expected)
    {
        if (response.Version != MultiLiveReservation.Version || response.Command is not (CreatedCommand or JoinedCommand) || response.Status != 1)
            throw new InvalidDataException("创建房间尚未收到成功入房同步。");
        var sync = DumpSerializer.Deserialize<MultiLiveRoomSyncData>(response.Payload);
        if (sync.roomId != roomId || sync.privateRoomNumber != roomNumber || sync.userId != userId || sync.ownerId != userId
            || sync.roomCreateTime == 0 || sync.releaseRoom)
            throw new InvalidDataException("入房同步与创建的私人房间或本玩家不一致。");
        var values = sync.playerProperty.values ?? throw new InvalidDataException("入房同步缺少本玩家属性。");
        if (SyncPropertyEncoding.ReadString(values[1]) != nickname || SyncPropertyEncoding.ReadInt32(values[5]) != 1
            || SyncPropertyEncoding.ReadString(values[7]) != "normal"
            || !DumpSerializer.Serialize(SyncPropertyEncoding.ReadObject<RoomUserBasicInfo>(values[2]))
                .AsSpan().SequenceEqual(DumpSerializer.Serialize(expected)))
            throw new InvalidDataException("入房同步的本玩家资料与发送内容不一致。");
        return sync;
    }
}
