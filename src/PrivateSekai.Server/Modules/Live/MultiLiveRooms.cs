extern alias game;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using game::CP.Realtime;
using game::Sekai;
using game::Sekai.MultiLive;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Transport.Realtime;

namespace PrivateSekai.Modules.Live;

// 房间为进程内状态，不写入用户存档；HTTP 与 UDP 共用同一把锁。
public sealed class MultiLiveRooms(TimeProvider clock, MasterData master)
{
    private readonly object gate = new();
    private readonly Dictionary<string, Room> rooms = [];

    public byte[] Number(long userId, string roomId)
    {
        lock (gate)
        {
            Prune();
            var room = Owned(userId, roomId);
            var number = room.Number;
            if (number == 0)
            {
                var used = rooms.Values.Select(r => r.Number).ToHashSet();
                if (used.Count >= 90000) throw new RoomConflictException("私人房号已用尽。");
                do { number = RandomNumberGenerator.GetInt32(10000, 100000); } while (used.Contains(number));
            }
            var encoded = DumpSerializer.Serialize(Payload(room, number, room.Lobby));
            room.Number = number;
            return encoded;
        }
    }

    public byte[] Update(long userId, string roomId, int lobbyId)
    {
        lock (gate)
        {
            Prune();
            var room = Owned(userId, roomId);
            if (room.Number == 0 || room.Players.Count != 0) throw new RoomConflictException("房间尚未分配房号或已入房。");
            var lobby = master.GetTable<MasterMultiLiveLobby>("multiLiveLobbies", r => r.id).FindById(lobbyId);
            if (lobby?.multiLiveLobbyType != "normal") throw new NotSupportedException("仅支持普通多人大厅。");
            var encoded = DumpSerializer.Serialize(Payload(room, room.Number, lobbyId));
            room.Lobby = lobbyId;
            return encoded;
        }
    }

    public byte[] Lookup(int number)
    {
        lock (gate)
        {
            Prune();
            var room = rooms.Values.SingleOrDefault(r => r.Number == number && r.Lobby.HasValue && r.Players.Count > 0)
                ?? throw new KeyNotFoundException("私人房间不存在。");
            return DumpSerializer.Serialize(Payload(room, room.Number, room.Lobby));
        }
    }

    public IReadOnlyList<RoomDelivery> Process(long userId, byte version, ushort command, byte[] payload)
    {
        lock (gate)
        {
            Prune();
            if (version == 2 && command == 3060) return Reserve(userId, payload);
            if (version == 2 && command is 3070 or 3080) return Enter(userId, command, payload);
            if (version == 1 && command == 102)
            {
                var room = Current(userId);
                var expected = Encoding.UTF8.GetBytes(room.Id + room.Id + Id(userId));
                if (!payload.AsSpan().SequenceEqual(expected)) throw new InvalidDataException("退出身份或房间不匹配。");
                return [new(userId, 1, 102, 1, []), .. Remove(userId)];
            }
            if (version != 2) throw new NotSupportedException("未支持的房间命令版本。");
            var current = Current(userId);
            if (command == 10010)
            {
                var delta = DumpSerializer.Deserialize<DynamicPropertyPayload>(payload);
                Validate(delta);
                if (delta.values.Keys.Any(k => k != 10)
                    || !delta.values.TryGetValue(10, out var number) || SyncPropertyEncoding.ReadInt64(number) != current.Number)
                    throw new NotSupportedException("当前房间仅开放房号同步。");
                var merged = Merge(current.Property, delta);
                var push = Frame(DumpSerializer.Serialize(delta));
                current.Property = merged;
                return current.Players.Keys.Select(id => new RoomDelivery(id, 2, 10010, 255, push)).ToArray();
            }
            if (command == 1021)
            {
                var requested = DumpSerializer.Deserialize<UserIdPayload>(payload);
                var player = current.Players.Values.SingleOrDefault(p => p.userId == requested.userId);
                if (player.userId == null) throw new InvalidDataException("索引查询不属于当前房间。");
                return [new(userId, 2, 1021, 1, DumpSerializer.Serialize(new UserIndexPayload { userId = player.userId, index = player.index }))];
            }
            throw new NotSupportedException("未支持的私人房间命令。");
        }
    }

    public IReadOnlyList<RoomDelivery> Disconnect(long userId)
    {
        lock (gate) return Remove(userId);
    }

    public bool HasPlayer(long userId)
    {
        lock (gate) return rooms.Values.Any(r => r.Players.ContainsKey(userId));
    }

    public void Prune()
    {
        lock (gate)
        {
            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            foreach (var room in rooms.Values.Where(r => r.Players.Count == 0 && now >= r.ExpiresAt).ToArray())
                rooms.Remove(room.Id);
        }
    }

    private IReadOnlyList<RoomDelivery> Reserve(long userId, byte[] payload)
    {
        var request = DumpSerializer.Deserialize<CreateMultiLivePrivateRoomData>(payload);
        if (request.multiLiveRuleType != "normal" || request.roomTTL <= 0)
            throw new NotSupportedException("仅支持具有有效 TTL 的普通私人房间。");
        Validate(request.roomProperty);
        if (rooms.Values.Any(r => r.Owner == userId)) throw new RoomConflictException("当前账号已持有房间。");
        if (rooms.Count >= 256) throw new RoomConflictException("私人房间容量已满。");
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var room = new Room
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(26)), Owner = userId,
            CreatedAt = now, ExpiresAt = checked(now + (long)request.roomTTL * 1000), Property = request.roomProperty
        };
        var response = DumpSerializer.Serialize(new CreateMultiLivePrivateRoomResponse
            { roomId = room.Id, roomCreateTime = checked((int)(now / 1000)) });
        rooms.Add(room.Id, room);
        return [new(userId, 2, 3060, 1, response)];
    }

    private IReadOnlyList<RoomDelivery> Enter(long userId, ushort command, byte[] payload)
    {
        string roomId;
        DynamicPropertyPayload property;
        int power;
        int? number = null;
        if (command == 3070)
        {
            var request = DumpSerializer.Deserialize<MultiLiveUnLockJoinData>(payload);
            roomId = request.roomId;
            property = request.playerProperty;
            power = request.totalPower;
            number = checked((int)request.privateRoomNumber);
            if (request.totalPowerLowerLimit.HasValue || request.totalPowerUpperLimit.HasValue)
                throw new NotSupportedException("当前私房不支持战力上下限。");
        }
        else
        {
            var request = DumpSerializer.Deserialize<MultiLiveDirectJoinData>(payload);
            roomId = request.roomId;
            property = request.playerProperty;
            power = request.totalPower;
        }
        var room = rooms.GetValueOrDefault(roomId) ?? throw new KeyNotFoundException("私人房间不存在。");
        if (!room.Lobby.HasValue || room.Number == 0 || rooms.Values.Any(r => r.Players.ContainsKey(userId)))
            throw new RoomConflictException("房间尚未准备完成或玩家已入房。");
        if (command == 3070 && (room.Owner != userId || room.Players.Count != 0 || number != room.Number))
            throw new InvalidDataException("创建账号或房号不匹配。");
        if (command == 3080 && (room.Players.Count == 0 || room.Players.Count >= 5))
            throw new RoomConflictException("房间未开放或已满员。");
        Validate(property);
        if (!new[] { 1, 2, 5, 7 }.All(property.values.ContainsKey)
            || SyncPropertyEncoding.ReadInt32(property.values[5]) != 1
            || SyncPropertyEncoding.ReadString(property.values[7]) != "normal"
            || SyncPropertyEncoding.ReadObject<RoomUserBasicInfo>(property.values[2]).TotalPowerIncludeBuff != power || power < 0)
            throw new InvalidDataException("私人房间玩家资料无效。");
        _ = SyncPropertyEncoding.ReadString(property.values[1]);
        var lobby = master.GetTable<MasterMultiLiveLobby>("multiLiveLobbies", r => r.id).FindById(room.Lobby.Value)!;
        if (power < lobby.totalPower) throw new RoomConflictException("玩家未达到大厅战力要求。");
        var player = new RoomSyncPlayer { userId = Id(userId), index = room.NextIndex, playerProperty = property };
        var sync = new MultiLiveRoomSyncData
        {
            userId = player.userId, index = player.index, playerProperty = property, roomId = room.Id,
            roomCreateTime = checked((uint)(room.CreatedAt / 1000)), ownerId = Id(room.Owner), roomProperty = room.Property,
            players = [.. room.Players.Values, player], privateRoomNumber = room.Number, releaseRoom = false
        };
        var result = new List<RoomDelivery> { new(userId, 2, 3001, 1, DumpSerializer.Serialize(sync)) };
        var push = Frame(DumpSerializer.Serialize(player));
        result.AddRange(room.Players.Keys.Select(id => new RoomDelivery(id, 2, 3001, 255, push)));
        room.Players.Add(userId, player);
        room.NextIndex++;
        return result;
    }

    private IReadOnlyList<RoomDelivery> Remove(long userId)
    {
        var room = rooms.Values.SingleOrDefault(r => r.Players.ContainsKey(userId));
        if (room == null) return [];
        var push = Frame(Encoding.UTF8.GetBytes(room.Id + Id(userId)));
        var result = room.Players.Keys.Where(id => id != userId)
            .Select(id => new RoomDelivery(id, 1, 102, 255, push)).ToList();
        room.Players.Remove(userId);
        // 尚未接入原服的房主转移协议；房主离开时关闭当前房间。
        if (room.Owner == userId)
        {
            result.AddRange(room.Players.Keys.Select(id => new RoomDelivery(id, 0, 0, 0, [], true)));
            rooms.Remove(room.Id);
        }
        return result;
    }

    private Room Owned(long userId, string roomId)
    {
        var room = rooms.GetValueOrDefault(roomId) ?? throw new KeyNotFoundException("私人房间不存在。");
        if (room.Owner != userId) throw new UnauthorizedAccessException("房间属于其他账号。");
        return room;
    }

    private Room Current(long userId) => rooms.Values.SingleOrDefault(r => r.Players.ContainsKey(userId))
        ?? throw new KeyNotFoundException("玩家尚未入房。");

    private static string Id(long userId) => userId.ToString(CultureInfo.InvariantCulture);
    private static PrivateRoomNumberPayload Payload(Room room, int number, int? lobby) => new()
    {
        privateRoomType = "multi_live", liveRuleType = "normal", roomId = room.Id,
        roomNo = number, createdAt = room.CreatedAt, multiLiveLobbyId = lobby
    };

    private static void Validate(DynamicPropertyPayload property)
    {
        if (property.values == null || property.values.Count == 0 || property.values.Count > 64 || property.isRSend != 1)
            throw new InvalidDataException("可靠同步属性无效。");
        foreach (var value in property.values.Values)
        {
            if (value == null || value.Length < 5 || value[0] is < 1 or > 7
                || BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(1)) != value.Length - 5)
                throw new InvalidDataException("同步属性类型或长度无效。");
        }
    }

    private static DynamicPropertyPayload Merge(DynamicPropertyPayload current, DynamicPropertyPayload delta)
    {
        var values = new Dictionary<int, byte[]>(current.values);
        foreach (var pair in delta.values) values[pair.Key] = pair.Value;
        return new() { isRSend = 1, values = values };
    }

    private static byte[] Frame(byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private sealed class Room
    {
        public required string Id;
        public long Owner;
        public long CreatedAt;
        public long ExpiresAt;
        public int Number;
        public int? Lobby;
        public int NextIndex = 1;
        public DynamicPropertyPayload Property;
        public Dictionary<long, RoomSyncPlayer> Players = [];
    }
}

public sealed record RoomDelivery(long UserId, byte Version, ushort Command, byte Status, byte[] Payload, bool Disconnect = false);

public sealed class RoomConflictException(string message) : Exception(message);
