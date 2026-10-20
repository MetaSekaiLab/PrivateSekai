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
            if (command == 3110)
            {
                if (payload.Length != 0) throw new InvalidDataException("再演请求必须为空载荷。");
                current.Closed = false;
                current.LiveId = null;
                current.Master = null;
                current.Prepared.Clear();
                return [];
            }
            if (command == 1013)
            {
                if (current.Owner != userId || !payload.AsSpan().SequenceEqual(new byte[] { 1 }))
                    throw new InvalidDataException("关闭匹配请求无效。");
                current.Closed = true;
                return [new(userId, 2, 1013, 1, [])];
            }
            if (command == 10000)
            {
                if (!payload.AsSpan().SequenceEqual(new byte[] { 1 })) throw new InvalidDataException("时钟请求无效。");
                return [new(userId, 2, 10000, 1, DumpSerializer.Serialize(new TimestampPayload
                    { timestamp = clock.GetUtcNow().ToUnixTimeMilliseconds() }))];
            }
            if (command == 10030)
            {
                var delta = DumpSerializer.Deserialize<DynamicPropertyPayload>(payload);
                Validate(delta);
                if (delta.values.Keys.Any(k => k is < 1 or > 11)) throw new NotSupportedException("未支持的玩家属性。");
                if (delta.values.TryGetValue(4, out var status) && !Enum.IsDefined((PlayerStatus)SyncPropertyEncoding.ReadInt32(status)))
                    throw new InvalidDataException("玩家状态无效。");
                if (delta.values.TryGetValue(4, out status) && SyncPropertyEncoding.ReadInt32(status) >= 9 && !current.Prepared.Contains(userId))
                    throw new RoomConflictException("玩家尚未完成HTTP演出准备。");
                var player = current.Players[userId];
                var push = Frame(DumpSerializer.Serialize(new RoomSyncPlayer
                    { userId = player.userId, index = player.index, playerProperty = delta }));
                player.playerProperty = Merge(player.playerProperty, delta);
                current.Players[userId] = player;
                return current.Players.Keys.Select(id => new RoomDelivery(id, 2, 10030, 255, push)).ToArray();
            }
            if (command == 10010)
            {
                var delta = DumpSerializer.Deserialize<DynamicPropertyPayload>(payload);
                Validate(delta);
                if (delta.values.Keys.Any(k => k is < 1 or > 18)
                    || userId != current.Owner && delta.values.Keys.Any(k => k != 10))
                    throw new UnauthorizedAccessException("仅房主可推进房间状态。");
                if (delta.values.TryGetValue(10, out var number) && SyncPropertyEncoding.ReadInt64(number) != current.Number)
                    throw new InvalidDataException("同步房号与已分配房号不符。");
                if (delta.values.TryGetValue(12, out var live) && SyncPropertyEncoding.ReadString(live) != (current.LiveId ?? ""))
                    throw new InvalidDataException("同步演出标识未完成HTTP准备。");
                if (delta.values.TryGetValue(2, out var step) && SyncPropertyEncoding.ReadInt32(step) >= 6
                    && (current.LiveId == null || current.Prepared.Count != current.Players.Count))
                    throw new RoomConflictException("演出成员尚未全部准备。");
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

    // 用户事务及响应编码完成后，才登记全场ID和成员准备状态。
    public byte[] PrepareOwner(long userId, MultiLiveRequest request, Func<string, byte[]> encode)
    {
        lock (gate)
        {
            var room = Current(userId);
            if (room.Owner != userId) throw new UnauthorizedAccessException("仅房主可申请全场演出。");
            if (room.LiveId != null || !room.Closed || room.Players.Count < 2)
                throw new RoomConflictException("房间尚未准备或已申请演出。");
            RequireRoom(room, request.photonRoomName, request.multiLiveLobbyId, request.privateFlg);
            if (!room.Property.values.TryGetValue(2, out var step) || SyncPropertyEncoding.ReadInt32(step) != 5
                || !room.Property.values.TryGetValue(4, out var music) || SyncPropertyEncoding.ReadInt32(music) != request.musicId
                || room.Players.Values.Any(p => Status(p) != PlayerStatus.LoadComplete))
                throw new RoomConflictException("双方尚未完成选曲加载。");
            var players = room.Players.Values.OrderBy(p => p.playerProperty.values.TryGetValue(3, out var order)
                && SyncPropertyEncoding.ReadInt32(order) > 0 ? SyncPropertyEncoding.ReadInt32(order) : p.index).ToArray();
            var ids = new[] { request.userId1, request.userId2, request.userId3, request.userId4, request.userId5 };
            var selected = new[] { request.selectedMusicId1, request.selectedMusicId2, request.selectedMusicId3, request.selectedMusicId4, request.selectedMusicId5 };
            for (var i = 0; i < ids.Length; i++)
            {
                var id = i < players.Length ? long.Parse(players[i].userId, CultureInfo.InvariantCulture) : 0;
                var selection = i < players.Length ? SyncPropertyEncoding.ReadInt32(players[i].playerProperty.values[8]) : 0;
                if (ids[i] != id || selected[i] != selection) throw new InvalidDataException("全场成员或选曲槽位不符。");
            }
            RequireDifficulty(room.Players[userId], request.musicDifficultyId);
            var liveId = Guid.NewGuid().ToString();
            var response = encode(liveId);
            room.LiveId = liveId;
            room.Master = request;
            room.Prepared.Add(userId);
            return response;
        }
    }

    public byte[] PrepareMember(long userId, string liveId, UserMultiLiveRequest request, Func<MultiLiveRequest, byte[]> encode)
    {
        lock (gate)
        {
            var room = Current(userId);
            if (room.Owner == userId || room.LiveId != liveId || room.Master == null || room.Prepared.Contains(userId))
                throw new RoomConflictException("本场演出不存在或成员已准备。");
            RequireRoom(room, request.photonRoomName, request.multiLiveLobbyId, request.privateFlg);
            if (room.Master.musicId != request.musicId) throw new InvalidDataException("成员歌曲与全场不符。");
            RequireDifficulty(room.Players[userId], request.musicDifficultyId);
            var response = encode(room.Master);
            room.Prepared.Add(userId);
            return response;
        }
    }

    private static void RequireRoom(Room room, string roomId, int lobbyId, bool privateRoom)
    {
        if (room.Id != roomId || room.Lobby != lobbyId || !privateRoom) throw new InvalidDataException("私人演出房间不符。");
    }

    private void RequireDifficulty(RoomSyncPlayer player, int difficultyId)
    {
        var difficulty = master.GetTable<MasterMusicDifficulty>("musicDifficulties", d => d.id).FindById(difficultyId);
        if (difficulty == null || SyncPropertyEncoding.ReadString(player.playerProperty.values[7]) != difficulty.musicDifficulty)
            throw new InvalidDataException("准备难度与玩家选择不符。");
    }

    private static PlayerStatus Status(RoomSyncPlayer player) => player.playerProperty.values.TryGetValue(4, out var value)
        ? (PlayerStatus)SyncPropertyEncoding.ReadInt32(value) : PlayerStatus.None;

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
        if (command == 3080 && (room.Players.Count == 0 || room.Players.Count >= 5 || room.Closed))
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
        if (room.Players.Count == 0) rooms.Remove(room.Id);
        else if (room.Owner == userId)
        {
            room.Owner = room.Players.MinBy(pair => pair.Value.index).Key;
            var changed = Frame(Encoding.UTF8.GetBytes(Id(room.Owner)));
            result.AddRange(room.Players.Keys.Select(id => new RoomDelivery(id, 2, 998, 255, changed)));
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
        public bool Closed;
        public string? LiveId;
        public MultiLiveRequest? Master;
        public HashSet<long> Prepared = [];
        public DynamicPropertyPayload Property;
        public Dictionary<long, RoomSyncPlayer> Players = [];
    }
}

public sealed record RoomDelivery(long UserId, byte Version, ushort Command, byte Status, byte[] Payload, bool Disconnect = false);

public sealed class RoomConflictException(string message) : Exception(message);
