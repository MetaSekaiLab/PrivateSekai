extern alias game;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using game::CP.Realtime;
using game::Sekai;
using RoomUserBasicInfo = game::Sekai.MultiLive.RoomUserBasicInfo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrivateSekai;
using PrivateSekai.Client;
using PrivateSekai.Client.Realtime;
using PrivateSekai.Config;
using PrivateSekai.Modules.Live;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;
using PrivateSekai.Transport;

internal static class RealtimeServerChecks
{
    public static async Task Run(string directory, Action<bool, string> check)
    {
        var masterPath = Path.Combine(directory, "realtime");
        Directory.CreateDirectory(masterPath);
        foreach (var table in Directory.EnumerateFiles(directory, "*.json"))
            File.Copy(table, Path.Combine(masterPath, Path.GetFileName(table)));
        // 隔离的虚构数据，用于验证结算事务及边界。
        var tables = new Dictionary<string, string>
        {
            ["cards"] = JsonSerializer.Serialize(Enumerable.Range(1, 10).Select(id => new { id, characterId = id, cardRarityType = id <= 5 ? "rarity_1" : "rarity_2" })),
            ["cardRarities"] = """[{"cardRarityType":"rarity_1","maxLevel":4,"trainingMaxLevel":4},{"cardRarityType":"rarity_2","maxLevel":5,"trainingMaxLevel":5}]""",
            ["levels"] = """[{"levelType":"user","level":1,"totalExp":0},{"levelType":"user","level":2,"totalExp":500},{"levelType":"user","level":3,"totalExp":10000},{"levelType":"card","level":1,"totalExp":0},{"levelType":"card","level":2,"totalExp":100},{"levelType":"card","level":3,"totalExp":300},{"levelType":"card","level":4,"totalExp":1000},{"levelType":"card","level":5,"totalExp":10000}]""",
            ["configs"] = """[{"configKey":"date_change_hour","value":"4"},{"configKey":"boost_recovery_max_count","value":"25"},{"configKey":"boost_recovery_second","value":"1800"},{"configKey":"rank_up_recover_boost_count","value":"10"}]""",
            ["boosts"] = """[{"id":1,"costBoost":0,"expRate":1,"rewardRate":1,"livePointRate":1},{"id":2,"costBoost":1,"expRate":5,"rewardRate":5,"livePointRate":5},{"id":3,"costBoost":2,"expRate":10,"rewardRate":10,"livePointRate":10},{"id":4,"costBoost":3,"isEventOnly":true,"expRate":15,"rewardRate":15,"livePointRate":15},{"id":7,"costBoost":6,"expRate":26,"rewardRate":26,"livePointRate":26}]""",
            ["playLevelScores"] = """[{"liveType":"solo","playLevel":21,"s":1000000,"a":800000,"b":400000,"c":80000},{"liveType":"multi_open","playLevel":21,"s":1000000,"a":800000,"b":400000,"c":50000}]""",
            ["liveMissionPeriods"] = """[{"id":1,"startAt":0,"endAt":4102444800000}]""",
            ["liveMissions"] = """[{"id":11,"liveMissionPeriodId":1,"liveMissionType":"free","requirement":11},{"id":12,"liveMissionPeriodId":1,"liveMissionType":"free","requirement":34}]""",
            ["musicAchievements"] = """[{"id":8,"musicAchievementType":"combo","musicAchievementTypeValue":"1","musicDifficultyType":"easy","resourceBoxId":8},{"id":9,"musicAchievementType":"combo","musicAchievementTypeValue":"0.25","musicDifficultyType":"normal","resourceBoxId":9},{"id":10,"musicAchievementType":"combo","musicAchievementTypeValue":"0.5","musicDifficultyType":"normal","resourceBoxId":10},{"id":11,"musicAchievementType":"combo","musicAchievementTypeValue":"0.75","musicDifficultyType":"normal","resourceBoxId":11},{"id":12,"musicAchievementType":"combo","musicAchievementTypeValue":"1","musicDifficultyType":"normal","resourceBoxId":12}]""",
            ["beginnerMissionV2s"] = "[]",
            ["honorMissions"] = """[{"id":777,"honorMissionType":"normal_full_combo","requirement":1}]""",
            ["characterMissionV2s"] = "[]",
            ["playerRankRewards"] = """[{"playerRank":2,"seq":1,"resourceBoxId":1}]""",
            ["releaseConditions"] = """[{"id":501,"releaseConditionType":"user_rank","releaseConditionTypeLevel":2}]"""
        };
        foreach (var pair in tables) File.WriteAllText(Path.Combine(masterPath, pair.Key + ".json"), pair.Value);
        var boxes = new List<MasterResourceBox>
        {
            new() { id = 99, resourceBoxPurpose = "score_rank_reward_detail", resourceBoxType = "expand", details = [new() { resourceType = "coin", resourceQuantity = 20 }] },
            new() { id = 100, resourceBoxPurpose = "score_rank_reward_detail", resourceBoxType = "expand", details = [new() { resourceType = "coin", resourceQuantity = 200 }] },
            new() { id = 101, resourceBoxPurpose = "score_rank_reward_detail", resourceBoxType = "expand", details = [new() { resourceType = "practice_ticket", resourceId = 1, resourceQuantity = 2 }] },
            new() { id = 1, resourceBoxPurpose = "player_rank_reward", resourceBoxType = "expand", details = [new() { resourceType = "jewel", resourceQuantity = 25 }] }
        };
        boxes.AddRange(Enumerable.Range(1, 5).Select(id => new MasterResourceBox
            { id = id, resourceBoxPurpose = "score_rank_reward_detail", resourceBoxType = "expand", details = [new() { resourceType = "material", resourceId = id, resourceQuantity = 2 }] }));
        boxes.Add(new() { id = 23, resourceBoxPurpose = "score_rank_reward_detail", resourceBoxType = "expand",
            details = [new() { resourceType = "material", resourceId = 11, resourceQuantity = 2 }] });
        boxes.AddRange(new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 12 }.Select(id => new MasterResourceBox
            { id = id + 10, resourceBoxPurpose = "score_rank_reward_detail", resourceBoxType = "expand", details = [new() { resourceType = "material", resourceId = id, resourceQuantity = 1 }] }));
        boxes.AddRange(Enumerable.Range(8, 5).Select(id => new MasterResourceBox
            { id = id, resourceBoxPurpose = "music_achievement", resourceBoxType = "expand", details = [new() { resourceType = id is 8 or 12 ? "coin" : "jewel", resourceQuantity = 5 }] }));
        File.WriteAllText(Path.Combine(masterPath, "resourceBoxes.json"), JsonSerializer.Serialize(boxes, DumpJson.Options));
        File.WriteAllText(Path.Combine(masterPath, "multiLiveLobbies.json"),
            """[{"id":1,"totalPower":0,"multiLiveLobbyType":"normal"}]""");
        File.WriteAllText(Path.Combine(masterPath, "musicDifficulties.json"),
            """[{"id":71,"musicId":7,"musicDifficulty":"easy","playLevel":5,"totalNoteCount":220},{"id":72,"musicId":7,"musicDifficulty":"normal","playLevel":10,"totalNoteCount":492},{"id":74,"musicId":7,"musicDifficulty":"expert","playLevel":21,"totalNoteCount":900},{"id":81,"musicId":8,"musicDifficulty":"easy","playLevel":5,"totalNoteCount":220},{"id":82,"musicId":8,"musicDifficulty":"normal","playLevel":10,"totalNoteCount":492},{"id":84,"musicId":8,"musicDifficulty":"expert","playLevel":21,"totalNoteCount":900}]""");
        File.WriteAllText(Path.Combine(masterPath, "musicVocals.json"), """[{"id":1,"musicId":7},{"id":2,"musicId":8}]""");
        File.WriteAllText(Path.Combine(masterPath, "musicCategories.json"), """[{"musicId":7,"musicCategoryName":"image"},{"musicId":8,"musicCategoryName":"image"}]""");
        File.WriteAllText(Path.Combine(masterPath, "limitedTimeMusics.json"), "[]");
        File.WriteAllText(Path.Combine(masterPath, "specialSeasons.json"), "[]");
        var store = new MemoryUserStore();
        for (var id = 11; id <= 13; id++)
            store.Save(id, new UserState { Data = new SuiteUser
            {
                userRegistration = new() { userId = id }, userGamedata = new() { userId = id, name = "测试用户" + id },
                userCards = [], userMaterials = [], userChargedCurrency = new() { free = 123, paid = 45 }
            } });
        var clock = new Clock();
        var rewardRandom = new RewardRandom();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        builder.Services.AddPrivateSekai().AddSingleton<IUserStore>(store)
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<Random>(rewardRandom)
            .AddSingleton(new RealtimeConfig { Enabled = true, Port = 0, IdleTimeoutSeconds = 60 })
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, masterPath));
        builder.Services.AddControllers().AddApplicationPart(typeof(DiarkisController).Assembly);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseMiddleware<PrskCryptoMiddleware>();
        app.MapControllers();
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancel = timeout.Token;
        var info = new RoomUserBasicInfo
        {
            CardId = 41, CardLevel = 1, TotalPowerIncludeBuff = 100,
            SubCardIds = [42, 43, 44, 45], SubCardSkillLv = [1, 1, 1, 1], SubCardImages = ["a", "b", "c", "d"],
            MemberCharacterRank = [], SubHonors = [], UserPlayerFramePartsLayout = []
        };
        var before11 = DumpSerializer.Serialize(store.Read(11)!.Data.userChargedCurrency);
        var before12 = DumpSerializer.Serialize(store.Read(12)!.Data.userChargedCurrency);
        var ownerAuth = await Auth(11);
        var guestAuth = await Auth(12);
        check(ownerAuth.sid != guestAuth.sid && ownerAuth.encryptionKey != guestAuth.encryptionKey
            && ownerAuth.udpPort == app.Services.GetRequiredService<PrivateSekai.Transport.Realtime.DiarkisServer>().BoundPort,
            "真实服务端为不同已存在用户签发独立实时凭证和实际监听端口");
        await Request(12, HttpMethod.Get, "/api/user/11/diarkis-auth?diarkisServerType=multi", null, 401);
        await Request(99, HttpMethod.Get, "/api/user/99/diarkis-auth?diarkisServerType=multi", null, 404);
        check(store.Read(99) == null, "实时认证拒绝越权和未知账号，不创建用户");
        using var owner = new DiarkisUdpClient(JsonSerializer.SerializeToNode(ownerAuth, DumpJson.Options)!.AsObject(), true);
        using var reservation = new DiarkisUdpClient(JsonSerializer.SerializeToNode(ownerAuth, DumpJson.Options)!.AsObject(), true);
        using var guest = new DiarkisUdpClient(JsonSerializer.SerializeToNode(guestAuth, DumpJson.Options)!.AsObject(), true);
        await Connect(reservation);
        await Connect(guest);
        var room = await Reserve(reservation);
        var roomId = room.roomId;
        await Request(12, HttpMethod.Post, $"/api/user/12/diarkis-room/{roomId}/no", NumberBody(), 403);
        var numbered = await Number(roomId);
        check(numbered.roomNo is >= 10000 and <= 99999 && numbered.roomId == roomId && numbered.createdAt == clock.GetUtcNow().ToUnixTimeMilliseconds(),
            "真实HTTP分配有效房号并保留预留房间及毫秒创建时间");
        check((await Number(roomId)).roomNo == numbered.roomNo, "重复申请同一预留房间房号保持一致");
        await Request(12, HttpMethod.Patch, $"/api/user/12/diarkis-room/{roomId}", PatchBody(1), 403);
        await Request(11, HttpMethod.Patch, $"/api/user/11/diarkis-room/{roomId}", PatchBody(999), 501);
        await Request(11, HttpMethod.Patch, $"/api/user/11/diarkis-room/{roomId}", PatchBody(1), 200);
        await Lookup(numbered.roomNo, 404);
        await Connect(owner);
        check(owner.ClientKeyAcknowledged, "预留连接与入房连接可使用同一凭证重新握手，不丢失预留房间");
        await owner.SendReliableAsync(2, 3070, MultiLiveRoom.CreateRequest(roomId, numbered.roomNo, "房主", info), cancel);
        var created = MultiLiveRoom.ParseCreated(await Response(owner, 2, 3001), roomId, numbered.roomNo, "11", "房主", info);
        check(created.players.Length == 1 && created.index > 0, "真实UDP创建完成后才登记房主和完整原始玩家属性");
        await SyncNumber(owner, numbered.roomNo);
        var looked = DumpSerializer.Deserialize<PrivateRoomNumberPayload>(await Lookup(numbered.roomNo, 200));
        check(looked.roomId == roomId && looked.multiLiveLobbyId == 1, "另一账号经真实HTTP按房号查到同一大厅和房间");
        await guest.SendReliableAsync(2, 3080, MultiLiveRoom.JoinRequest(roomId, "成员", info), cancel);
        var joined = MultiLiveRoom.ParseCreated(await Response(guest, 2, 3001), roomId, numbered.roomNo, "12", "成员", info, "11");
        var member = MultiLiveRoom.ReadMemberJoins(await Response(owner, 2, 3001, 255)).Single();
        check(joined.players.Length == 2 && joined.players.Select(p => p.userId).ToHashSet().SetEquals(["11", "12"])
            && member.userId == "12" && member.index == joined.index,
            "双账号经真实UDP入房，双方收到一致的成员身份、索引和资料");
        MultiLiveRoom.RequirePlayer(member.playerProperty, MultiLiveRoom.PlayerProperty("成员", info));
        await SyncNumber(guest, numbered.roomNo);
        await owner.SendReliableAsync(2, 1021, MultiLiveRoom.UserIndexRequest("12"), cancel);
        check(MultiLiveRoom.ParseUserIndex(await Response(owner, 2, 1021), "12") == joined.index, "服务端索引查询只返回当前房间真实成员");
        await Leave(guest, "12");
        check(MultiLiveRoom.ReadMemberLeaves(await Response(owner, 1, 102, 255), roomId).SequenceEqual(["12"]),
            "成员退出后房主实际收到带当前房间和成员身份的退出推送");
        await Leave(owner, "11");
        await Lookup(numbered.roomNo, 404);
        using (var independent = new HttpClient { BaseAddress = http.BaseAddress })
        {
            foreach (var id in new[] { 11, 12 })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/suite/user/{id}");
                request.Headers.Add("X-Session-Token", JwtSignature.GenSessionToken(id));
                using var response = await independent.SendAsync(request, cancel);
                check(response.StatusCode == HttpStatusCode.OK, "退出后独立HTTP会话可以重新回读真实Suite接口");
                var suite = DumpSerializer.Deserialize<SuiteUser>(PrskCrypto.DecryptAesCbc(await response.Content.ReadAsByteArrayAsync(cancel)));
                check(suite.userRegistration.userId == id && suite.userCards.Length == 0
                    && suite.userChargedCurrency.free == 123 && suite.userChargedCurrency.paid == 45,
                    "独立完整Suite回读的账号、卡牌和两类水晶与建房前一致");
            }
        }
        check(DumpSerializer.Serialize(store.Read(11)!.Data.userChargedCurrency).AsSpan().SequenceEqual(before11)
            && DumpSerializer.Serialize(store.Read(12)!.Data.userChargedCurrency).AsSpan().SequenceEqual(before12)
            && store.Read(11)!.Data.userCards.Length == 0 && store.Read(12)!.Data.userCards.Length == 0,
            "完整建房加入退出不改变持久水晶和卡牌");

        // 成员失联应清除成员，活跃房主仍能查房及正常退出。
        var again = await Reserve(owner);
        roomId = again.roomId;
        numbered = await Number(roomId);
        await Request(11, HttpMethod.Patch, $"/api/user/11/diarkis-room/{roomId}", PatchBody(1), 200);
        await owner.SendReliableAsync(2, 3070, MultiLiveRoom.CreateRequest(roomId, numbered.roomNo, "房主", info), cancel);
        await Response(owner, 2, 3001);
        await guest.SendReliableAsync(2, 3080, MultiLiveRoom.JoinRequest(roomId, "成员", info), cancel);
        await Response(guest, 2, 3001);
        await Response(owner, 2, 3001, 255);
        await guest.SendUnreliableAsync(0, 3, new byte[8], cancel);
        await Response(guest, 0, 3);
        clock.Advance(TimeSpan.FromSeconds(30));
        await owner.SendUnreliableAsync(0, 3, new byte[8], cancel);
        await Response(owner, 0, 3);
        clock.Advance(TimeSpan.FromSeconds(31));
        check(MultiLiveRoom.ReadMemberLeaves(await Response(owner, 1, 102, 255), roomId).SequenceEqual(["12"]),
            "真实UDP空闲成员超时清理，活跃房主实际收到退出通知");
        await Lookup(numbered.roomNo, 200);
        await Leave(owner, "11");
        await Lookup(numbered.roomNo, 404);

        // 伪造退出身份不能移除房主，违规连接清理后房主仍可退出。
        using (var replacement = new DiarkisUdpClient(JsonSerializer.SerializeToNode(await Auth(12), DumpJson.Options)!.AsObject(), true))
        {
            await Connect(replacement);
            await Open(replacement);
            await replacement.SendReliableAsync(1, 102, MultiLiveRoom.LeaveRequest(roomId, "11"), cancel);
            check(MultiLiveRoom.ReadMemberLeaves(await Response(owner, 1, 102, 255), roomId).SequenceEqual(["12"]),
                "伪造退出只会关闭违规连接，不会移除被冒充的房主");
            await Lookup(numbered.roomNo, 200);
            await Leave(owner, "11");
            await Lookup(numbered.roomNo, 404);
        }
        using (var replacement = new DiarkisUdpClient(JsonSerializer.SerializeToNode(await Auth(12), DumpJson.Options)!.AsObject(), true))
        {
            await Connect(replacement);
            await Open(replacement);
            await Leave(owner, "11");
            var stopped = false;
            try { while (true) await replacement.ReceiveAsync(cancel); }
            catch (IOException) { stopped = true; }
            check(stopped, "当前本地房主退出策略关闭房间及成员连接，不保留僵尸成员");
            await Lookup(numbered.roomNo, 404);
        }

        // 只含 userId 的签名或凭证不能替代实时会话。
        using (var signatureRequest = new HttpRequestMessage(HttpMethod.Get, "/api/user/11/diarkis-auth?diarkisServerType=multi"))
        {
            signatureRequest.Headers.Add("X-Session-Token", JwtSignature.GenUserSignature(11));
            using var rejected = await http.SendAsync(signatureRequest, cancel);
            check(rejected.StatusCode == HttpStatusCode.Unauthorized, "普通用户签名不能冒充已认证会话取得实时密钥");
        }

        await StartLive();
        await PlayLive();
        await PlayRankC();
        await PlayLifeZero();
        await PlayMixedDifficulty();
        await PlayReplay();
        await PlayBoost();
        CheckDailyBoundary();

        // 原始UDP重传同一分片序列，预留只应用一次；错序也由接收序号恢复。
        await DuplicateReservation();
        await app.StopAsync();

        async Task<(game::Sekai.MultiLive.MultiLiveRoomSyncData Owner, game::Sekai.MultiLive.MultiLiveRoomSyncData Guest, RoomSyncPlayer Seen)> Open(DiarkisUdpClient member)
        {
            roomId = (await Reserve(owner)).roomId;
            numbered = await Number(roomId);
            await Request(11, HttpMethod.Patch, $"/api/user/11/diarkis-room/{roomId}", PatchBody(1), 200);
            await owner.SendReliableAsync(2, 3070, MultiLiveRoom.CreateRequest(roomId, numbered.roomNo, "房主", info), cancel);
            var own = DumpSerializer.Deserialize<game::Sekai.MultiLive.MultiLiveRoomSyncData>((await Response(owner, 2, 3001)).Payload);
            await member.SendReliableAsync(2, 3080, MultiLiveRoom.JoinRequest(roomId, "成员", info), cancel);
            var joined = DumpSerializer.Deserialize<game::Sekai.MultiLive.MultiLiveRoomSyncData>((await Response(member, 2, 3001)).Payload);
            var seen = MultiLiveRoom.ReadMemberJoins(await Response(owner, 2, 3001, 255)).Single();
            return (own, joined, seen);
        }

        async Task StartLive()
        {
            foreach (var id in new[] { 11, 12 })
            {
                var state = store.Read(id)!;
                state.Data.userGamedata.deck = 1;
                state.Data.userGamedata.rank = 1;
                state.Data.userBoost = new() { current = 10, recoveryAt = (ulong)clock.GetUtcNow().ToUnixTimeMilliseconds() };
                state.Data.userCards = Enumerable.Range(1, 5).Select(cardId => new UserCard { cardId = cardId, level = 1 }).ToArray();
                state.Data.userDecks = [new() { deckId = 1, leader = 1, member1 = 1, member2 = 2, member3 = 3, member4 = 4, member5 = 5 }];
                store.Save(id, state);
            }
            using var member = new DiarkisUdpClient(JsonSerializer.SerializeToNode(await Auth(12), DumpJson.Options)!.AsObject(), true);
            await Connect(member);
            var room = await Open(member);
            var song = new MultiLiveSong(7, 71, 1, "easy");
            var request = MultiLiveFlow.PrepareRequest(store.Read(11)!.Data, roomId, 1, song, [11, 12]);
            await Request(11, HttpMethod.Post, "/api/multi-live", DumpSerializer.Serialize(request), 409);
            var wrongOwner = DumpSerializer.Deserialize<MultiLiveRequest>(DumpSerializer.Serialize(request));
            wrongOwner.userId = 12;
            await Request(12, HttpMethod.Post, "/api/multi-live", DumpSerializer.Serialize(wrongOwner), 403);
            check(store.Read(11)!.Private.MultiLiveSessions.Count == 0, "提前申请和非房主申请不创建演出存档");

            // 独立检查编码失败回滚；随后完整协议流程仍需成功申请同一房间。
            var rooms = app.Services.GetRequiredService<MultiLiveRooms>();
            rooms.Process(11, 2, 1013, [1]);
            rooms.Process(11, 2, 10010, DumpSerializer.Serialize(MultiLiveFlow.Property(
                (2, SyncPropertyEncoding.Int32(5)), (4, SyncPropertyEncoding.Int32(7)))));
            foreach (var id in new long[] { 11, 12 })
                rooms.Process(id, 2, 10030, DumpSerializer.Serialize(MultiLiveFlow.Property(
                    (4, SyncPropertyEncoding.Int32(8)), (8, SyncPropertyEncoding.Int32(7)), (7, SyncPropertyEncoding.String("easy")))));
            foreach (var count in new[] { -1, 3, 99 })
            {
                request.boostCount = count;
                await Request(11, HttpMethod.Post, "/api/multi-live", DumpSerializer.Serialize(request), 400);
            }
            var funded = store.Read(11)!;
            var empty = funded.DeepClone();
            empty.Data.userBoost.current = 0;
            store.Save(11, empty);
            request.boostCount = 2;
            using (var scope = app.Services.CreateScope())
            {
                var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
                var live = scope.ServiceProvider.GetRequiredService<MultiLiveService>();
                var failed = false;
                try
                {
                    rooms.PrepareOwner(11, request, liveId => operations.Execute(11, () =>
                    {
                        live.Prepare(liveId, request, MultiLiveFlow.MemberRequest(store.Read(11)!.Data, roomId, 1, song, 2));
                        return new BrokenResponse();
                    }));
                }
                catch (MessagePack.MessagePackSerializationException) { failed = true; }
                check(failed && store.Read(11)!.Private.MultiLiveSessions.Count == 0 && store.Read(11)!.Data.userBoost.current == 0,
                    "不足体力仍允许准备；编码失败同时回滚用户会话与全场演出标识");
            }
            store.Save(11, funded);
            request.boostCount = 0;

            var output = Path.Combine(masterPath, "flow");
            Directory.CreateDirectory(output);
            var report = new JsonObject();
            using var ownerHttp = Client(11);
            using var guestHttp = Client(12);
            var before = await ownerHttp.Suite();
            var guestBefore = await guestHttp.Suite();
            check(await MultiLiveFlow.Run(owner, member, ownerHttp, guestHttp, store.Read(11)!.Data, store.Read(12)!.Data,
                room.Owner, room.Guest, room.Seen, 1, song, output, report, cancel)
                && report["complete"]!.GetValue<bool>() && report["phase"]!.GetValue<string>() == "started"
                && report["masterPrepared"]!.GetValue<bool>() && report["memberPrepared"]!.GetValue<bool>()
                && report["serverClocksSynchronized"]!.GetValue<bool>(),
                "现有Client完整状态链贯通真实Server双方选曲、HTTP准备、时钟及开始推送");
            var ownerSession = store.Read(11)!.Private.MultiLiveSessions.Single();
            var guestSession = store.Read(12)!.Private.MultiLiveSessions.Single();
            check(ownerSession.Key == guestSession.Key && ownerSession.Value.Master.userId1 == 11
                && guestSession.Value.Start.musicDifficultyId == 71 && ownerSession.Value.Start.boostCount == 0,
                "双方私有演出存档保存同一全场ID、原始成员槽位及各自准备参数");
            var database = Path.Combine(masterPath, "multi-live.db");
            using (var persisted = new SqliteUserStore(database))
            {
                persisted.Save(11, store.Read(11)!);
                persisted.Save(12, store.Read(12)!);
            }
            using (var reopened = new SqliteUserStore(database))
            {
                check(reopened.Read(11)!.Private.MultiLiveSessions.Single().Key == ownerSession.Key
                    && reopened.Read(12)!.Private.MultiLiveSessions.Single().Value.Start.musicDifficultyId == 71,
                    "SQLite重启后恢复双方相同演出ID及原始成员准备模型");
            }
            await Request(11, HttpMethod.Post, "/api/multi-live", DumpSerializer.Serialize(request), 409);
            await Request(12, HttpMethod.Post, $"/api/user/12/multi-live/{ownerSession.Key}",
                DumpSerializer.Serialize(MultiLiveFlow.MemberRequest(store.Read(12)!.Data, roomId, 1, song)), 409);
            await Leave(member, "12");
            await Response(owner, 1, 102, 255);
            await Leave(owner, "11");
            await Lookup(numbered.roomNo, 404);
            using var independent = Client(11);
            using var guestIndependent = Client(12);
            var after = await independent.Suite();
            var guestAfter = await guestIndependent.Suite();
            check(JsonNode.DeepEquals(before["userCards"], after["userCards"])
                && JsonNode.DeepEquals(before["userChargedCurrency"], after["userChargedCurrency"])
                && JsonNode.DeepEquals(guestBefore["userCards"], guestAfter["userCards"])
                && JsonNode.DeepEquals(guestBefore["userChargedCurrency"], guestAfter["userChargedCurrency"]),
                "开始演出后双方退出及独立Suite回读卡牌和水晶不变");

        }

        async Task PlayLive()
        {
            var song = new MultiLiveSong(7, 71, 1, "easy");
            var score = new UserMultiLiveClearScoreRequest { score = 100000, perfectCount = 210, goodCount = 10, maxCombo = 210, tapCount = 220, life = 1000 };
            var performance = new MultiLivePerformance { DurationMilliseconds = 1, OwnerScore = score, GuestScore = score };
            var old = store.Read(11)!;
            var oldId = old.Private.MultiLiveSessions.Single().Key;
            var request = MultiLivePerformance.Result([11, 12], new Dictionary<long, UserMultiLiveClearScoreRequest>
                { [11] = MultiLivePerformance.Score(score, 11, 71), [12] = MultiLivePerformance.Score(score, 12, 71) });
            request.totalScore++;
            await Request(11, HttpMethod.Put, $"/api/user/11/multi-live/{oldId}", DumpSerializer.Serialize(request), 400);
            request.totalScore--;
            await Request(12, HttpMethod.Put, $"/api/user/11/multi-live/{oldId}", DumpSerializer.Serialize(request), 401);
            using (var scope = app.Services.CreateScope())
            {
                var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
                var service = scope.ServiceProvider.GetRequiredService<MultiLiveService>();
                var failed = false;
                try { operations.Execute(11, () => { service.Clear(oldId, request); return new BrokenResponse(); }); }
                catch (MessagePack.MessagePackSerializationException) { failed = true; }
                check(failed && store.Read(11)!.Private.MultiLiveSessions.ContainsKey(oldId)
                    && store.Read(11)!.Private.DailyLivePoint.BonusCount == 0 && store.Read(11)!.Data.userGamedata.totalExp == 0
                    && store.Read(11)!.Data.userCards.All(c => c.totalExp == 0), "结算编码失败同时回滚会话删除、每日次数和全部经验");
                var overflow = store.Read(11)!;
                overflow.Data.userGamedata.coin = int.MaxValue;
                store.Save(11, overflow);
                failed = false;
                try { operations.Execute(11, () => service.Clear(oldId, request)); }
                catch (OverflowException) { failed = true; }
                check(failed && store.Read(11)!.Private.MultiLiveSessions.ContainsKey(oldId)
                    && store.Read(11)!.Data.userGamedata.totalExp == 0 && store.Read(11)!.Data.userCards.All(c => c.totalExp == 0),
                    "后续奖励溢出回滚前面经验与会话删除");
                store.Save(11, old);
            }
            for (var round = 0; round < 4; round++)
            {
                using var member = new DiarkisUdpClient(JsonSerializer.SerializeToNode(await Auth(12), DumpJson.Options)!.AsObject(), true);
                await Connect(member);
                var room = await Open(member);
                using var ownerHttp = Client(11);
                using var guestHttp = Client(12);
                var output = Path.Combine(masterPath, "clear-" + round);
                Directory.CreateDirectory(output);
                var report = new JsonObject();
                check(await MultiLiveFlow.Run(owner, member, ownerHttp, guestHttp, store.Read(11)!.Data, store.Read(12)!.Data,
                    room.Owner, room.Guest, room.Seen, 1, song, output, report, cancel, performance)
                    && report["phase"]!.GetValue<string>() == "cleared", "现有Client受控演出从真实Server准备贯通双方结算");
                foreach (var side in new[] { "ownerResult", "guestResult" })
                {
                    var point = report[side]!["userLivePoint"]!;
                    check(report[side]!["scoreRank"]!.GetValue<string>() == "rank_c"
                        && report[side]!["multiScoreRank"]!.GetValue<string>() == "rank_d"
                        && point["livePointBonusRemaining"]!.GetValue<int>() == Math.Max(0, 2 - round)
                        && (point["addDailyBonusProgress"]?.GetValue<int>() ?? 0) == (round < 3 ? 10 : 0),
                        "双方同日四场逐次获得3份每日加成，第四场不再领取");
                }
                var cleared = ownerHttp.LastResponse!;
                if (round is 0 or 3)
                    check(cleared["updatedResources"]!["userLiveMissions"]![0]!["achievedMissionIds"]!.AsArray()
                        .Select(v => v!.GetValue<int>()).SequenceEqual([round == 0 ? 11 : 12]), "Live任务跨门槛返回本次达成提示");
                var liveId = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "live-state.local.json")))!["liveId"]!.GetValue<string>();
                await Request(11, HttpMethod.Put, $"/api/user/11/multi-live/{liveId}", DumpSerializer.Serialize(MultiLivePerformance.Result([11, 12],
                    new Dictionary<long, UserMultiLiveClearScoreRequest> { [11] = MultiLivePerformance.Score(score, 11, 71), [12] = MultiLivePerformance.Score(score, 12, 71) })), 404);
                await Leave(member, "12");
                await Response(owner, 1, 102, 255);
                await Leave(owner, "11");
                using var independent = Client(11);
                using var guestIndependent = Client(12);
                foreach (var client in new[] { independent, guestIndependent })
                {
                    var suite = await client.Suite();
                    var data = JsonSerializer.Deserialize<SuiteUser>(suite.ToJsonString(), DumpJson.Options)!;
                    check(data.userGamedata.totalExp == (round + 1) * 20 && data.userGamedata.coin == (round + 1) * 20
                        && data.userCards.All(c => c.totalExp == (round + 1) * 60)
                        && data.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress == (round < 3 ? (round + 1) * 11 : 34)
                        && data.userLiveMissions.All(m => m.achievedMissionIds == null || m.achievedMissionIds.Length == 0)
                        && data.userBoost.current == 10 && data.userMusicResults.Single(r => r.playType == "multi").mvpCount == round + 1,
                        "双方退出后独立Suite确认经验、卡牌、金币、Live P和MVP仅结算一次，体力不变");
                }
            }
            var database = Path.Combine(masterPath, "daily-point.db");
            using (var saved = new SqliteUserStore(database)) saved.Save(11, store.Read(11)!);
            using (var reopened = new SqliteUserStore(database))
                check(reopened.Read(11)!.Private.DailyLivePoint.BonusCount == 3
                    && reopened.Read(11)!.Private.MultiLiveSessions.Count == 0, "SQLite重启保留每日次数并确认已消费演出会话");
        }

        async Task PlayRankC()
        {
            var song = new MultiLiveSong(7, 71, 1, "easy");
            var ownerScore = new UserMultiLiveClearScoreRequest { score = 100000, perfectCount = 210, goodCount = 10, maxCombo = 210, tapCount = 220, life = 1000 };
            var guestScore = new UserMultiLiveClearScoreRequest { score = 150000, perfectCount = 210, goodCount = 10, maxCombo = 210, tapCount = 220, life = 1000 };
            var result = MultiLivePerformance.Result([11, 12], new Dictionary<long, UserMultiLiveClearScoreRequest>
                { [11] = MultiLivePerformance.Score(ownerScore, 11, 71), [12] = MultiLivePerformance.Score(guestScore, 12, 71) });
            var performance = new MultiLivePerformance { DurationMilliseconds = 1, OwnerScore = ownerScore, GuestScore = guestScore };
            for (var round = 0; round < 3; round++)
            {
                rewardRandom.Choice = round;
                using var member = new DiarkisUdpClient(JsonSerializer.SerializeToNode(await Auth(12), DumpJson.Options)!.AsObject(), true);
                await Connect(member);
                var room = await Open(member);
                using var ownerHttp = Client(11);
                using var guestHttp = Client(12);
                var beforeOwner = store.Read(11)!;
                var beforeGuest = store.Read(12)!;
                var output = Path.Combine(masterPath, "rank-c-" + round);
                Directory.CreateDirectory(output);
                var report = new JsonObject();
                check(await MultiLiveFlow.Run(owner, member, ownerHttp, guestHttp, beforeOwner.Data, beforeGuest.Data,
                    room.Owner, room.Guest, room.Seen, 1, song, output, report, cancel, performance)
                    && report["phase"]!.GetValue<string>() == "cleared", "现有Client贯通全场C双边成绩、结算及资源刷新");
                var liveId = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "live-state.local.json")))!["liveId"]!.GetValue<string>();
                foreach (var id in new[] { 11, 12 })
                    await Request(id, HttpMethod.Put, $"/api/user/{id}/multi-live/{liveId}", DumpSerializer.Serialize(result), 404);
                await Leave(member, "12");
                await Response(owner, 1, 102, 255);
                await Leave(owner, "11");
                await Lookup(numbered.roomNo, 404);
                var materialRewards = round switch
                {
                    0 => new Dictionary<int, int> { [1] = 2, [2] = 1, [7] = 1, [12] = 1 },
                    1 => new Dictionary<int, int> { [3] = 2, [2] = 1, [7] = 1 },
                    _ => new Dictionary<int, int> { [3] = 2, [5] = 1, [8] = 1 }
                };
                foreach (var (id, before, response) in new[] { (11, beforeOwner, ownerHttp.LastResponse!), (12, beforeGuest, guestHttp.LastResponse!) })
                {
                    var exp = JsonSerializer.Deserialize<UpdateExpResult>(response["userExpResult"]!.ToJsonString(), DumpJson.Options)!;
                    var cardExp = JsonSerializer.Deserialize<DeckCardUpdateExpResult[]>(response["deckCardExpResults"]!.ToJsonString(), DumpJson.Options)!;
                    var points = JsonSerializer.Deserialize<UserLivePoint>(response["userLivePoint"]!.ToJsonString(), DumpJson.Options)!;
                    check(response["scoreRank"]!.GetValue<string>() == "rank_c" && response["multiScoreRank"]!.GetValue<string>() == "rank_c"
                        && response["totalScore"]!.GetValue<int>() == 250000 && exp.afterTotalExp - exp.beforeTotalExp == 200
                        && cardExp.All(c => c.expResult.afterTotalExp == Math.Min(1000, c.expResult.beforeTotalExp + 600))
                        && (round != 0 || cardExp.All(c => c.expResult.afterTotalExp - c.expResult.beforeTotalExp == 600)),
                        "全场C返回200玩家经验和600卡牌经验，并遵守卡牌等级上限");
                    check(response["user1"]!["mvpFlg"]?.GetValue<bool>() != true && response["user2"]!["mvpFlg"]!.GetValue<bool>()
                        && response["user1"]!["fullComboFlg"]?.GetValue<bool>() != true && response["user2"]!["fullComboFlg"]?.GetValue<bool>() != true
                        && points.addDailyBonusProgress == 0 && points.livePointBonusRemaining == 0,
                        "不同成绩仅最高分成员获得MVP，已用尽的每日名额不再增加");
                    var rewards = JsonSerializer.Deserialize<UserResource[]>(response["scoreRankRewards"]!.ToJsonString(), DumpJson.Options)!;
                    check(rewards.Single(r => r.resourceType == "coin").quantity == 200
                        && rewards.Single(r => r.resourceType == "practice_ticket").resourceId == 1
                        && rewards.Single(r => r.resourceType == "practice_ticket").quantity == 2
                        && rewards.Count(r => r.resourceType == "material") == materialRewards.Count
                        && materialRewards.All(p => rewards.Single(r => r.resourceType == "material" && r.resourceId == p.Key).quantity == p.Value),
                        "全场C覆盖三组实际奖励，含可选服装材料");
                    using var independent = Client(id);
                    var suite = JsonSerializer.Deserialize<SuiteUser>((await independent.Suite()).ToJsonString(), DumpJson.Options)!;
                    check(suite.userGamedata.totalExp == 80 + (round + 1) * 200 && suite.userGamedata.coin == 80 + (round + 1) * 200
                        && suite.userCards.All(c => c.totalExp == Math.Min(1000, 240 + (round + 1) * 600))
                        && suite.userPracticeTickets.Single(t => t.practiceTicketId == 1).quantity == (round + 1) * 2,
                        "双方退出后独立Suite确认经验、金币、练习券和卡牌仅入账一次");
                    var oldMaterials = (before.Data.userMaterials ?? []).ToDictionary(m => m.materialId, m => m.quantity);
                    check(suite.userMaterials.All(m => m.quantity - oldMaterials.GetValueOrDefault(m.materialId) == materialRewards.GetValueOrDefault(m.materialId))
                        && materialRewards.All(p => suite.userMaterials.Any(m => m.materialId == p.Key))
                        && suite.userMusicResults.Single(r => r.playType == "multi").mvpCount == (id == 11 ? 4 : 5 + round)
                        && suite.userHonorMissions.Single(m => m.honorMissionType == "multi_live_mvp").progress == (id == 11 ? 4 : 5 + round)
                        && suite.userMusicResults.Single(r => r.playType == "multi").highScore == (id == 11 ? 100000 : 150000)
                        && suite.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress == 35 + round,
                        "双方回读材料增量逐项吻合、MVP和最高分分别累计，任务继续推进");
                    check(suite.userGamedata.rank == (round == 2 ? 2 : 1) && suite.userBoost.current == (round == 2 ? 20 : 10)
                        && suite.userChargedCurrency.free == (round == 2 ? 148 : 123) && suite.userChargedCurrency.paid == 45
                        && (round != 2 || suite.userReleaseConditions.Any(c => c.releaseConditionId == 501)),
                        "C档升级衔接等级奖励、体力恢复和等级解锁，未升级时保持原值");
                }
                if (round == 1)
                {
                    var state = store.Read(11)!;
                    state.Private.MultiLiveSessions.Add(liveId, new()
                    {
                        Master = MultiLiveFlow.PrepareRequest(state.Data, roomId, 1, song, [11, 12]),
                        Start = MultiLiveFlow.MemberRequest(state.Data, roomId, 1, song), CreatedAt = clock.GetUtcNow().ToUnixTimeMilliseconds()
                    });
                    store.Save(11, state);
                    using var scope = app.Services.CreateScope();
                    var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
                    var service = scope.ServiceProvider.GetRequiredService<MultiLiveService>();
                    var snapshot = DumpSerializer.Serialize(state.Data);
                    var failed = false;
                    try { operations.Execute(11, () => { service.Clear(liveId, result); return new BrokenResponse(); }); }
                    catch (MessagePack.MessagePackSerializationException) { failed = true; }
                    check(failed && DumpSerializer.Serialize(store.Read(11)!.Data).AsSpan().SequenceEqual(snapshot)
                        && store.Read(11)!.Private.MultiLiveSessions.ContainsKey(liveId)
                        && store.Read(11)!.Private.DailyLivePoint.BonusCount == 3,
                        "C档编码失败回滚升级、所有资源、任务、每日次数和会话删除");
                    var overflow = state.DeepClone();
                    overflow.Data.userPracticeTickets.Single(t => t.practiceTicketId == 1).quantity = int.MaxValue;
                    store.Save(11, overflow);
                    snapshot = DumpSerializer.Serialize(overflow.Data);
                    failed = false;
                    try { operations.Execute(11, () => service.Clear(liveId, result)); }
                    catch (OverflowException) { failed = true; }
                    check(failed && DumpSerializer.Serialize(store.Read(11)!.Data).AsSpan().SequenceEqual(snapshot)
                        && store.Read(11)!.Private.MultiLiveSessions.ContainsKey(liveId),
                        "C档练习券溢出回滚此前金币、等级奖励及卡牌经验，会话仍可处理");
                    state.Private.MultiLiveSessions.Clear();
                    store.Save(11, state);
                }
            }
            var database = Path.Combine(masterPath, "rank-c.db");
            using (var saved = new SqliteUserStore(database))
                foreach (var id in new[] { 11, 12 }) saved.Save(id, store.Read(id)!);
            using (var reopened = new SqliteUserStore(database))
                foreach (var id in new[] { 11, 12 })
                {
                    var restored = reopened.Read(id)!;
                    var expected = store.Read(id)!.Data;
                    // SQLite只保存持久字段，排除响应时间和刷新标记。
                    expected.now = restored.Data.now;
                    expected.refreshableTypes = restored.Data.refreshableTypes;
                    check(DumpSerializer.Serialize(restored.Data).AsSpan().SequenceEqual(DumpSerializer.Serialize(expected))
                        && restored.Private.MultiLiveSessions.Count == 0 && restored.Private.DailyLivePoint.BonusCount == 3,
                        "SQLite重启恢复双账号C档完整状态、每日次数及已消费会话");
                }
        }

        async Task PlayLifeZero()
        {
            var song = new MultiLiveSong(7, 72, 1, "normal");
            foreach (var id in new[] { 11, 12 })
            {
                var state = store.Read(id)!;
                state.Private.DailyLivePoint.BonusCount = 0;
                var card = state.Data.userCards.Single(c => c.cardId == 1);
                card.totalExp = 240;
                card.level = 2;
                card.exp = 140;
                store.Save(id, state);
            }
            for (var round = 0; round < 6; round++)
            {
                rewardRandom.Choice = round + 3;
                var guestMvp = round % 3 == 1;
                if (round == 3)
                    foreach (var id in new[] { 11, 12 })
                    {
                        var state = store.Read(id)!;
                        state.Data.userMusicResults = state.Data.userMusicResults.Where(r => r.musicDifficultyType != "normal").ToArray();
                        store.Save(id, state);
                    }
                var ownerScore = new UserMultiLiveClearScoreRequest { score = guestMvp ? 100000 : 150000,
                    perfectCount = 472, missCount = 20, maxCombo = 472, tapCount = 472, life = 0 };
                var guestScore = new UserMultiLiveClearScoreRequest { score = guestMvp ? 150000 : 100000,
                    perfectCount = round < 3 ? 482 : 472, goodCount = round < 3 ? 10 : 0, missCount = round < 3 ? 0 : 20,
                    maxCombo = round < 3 ? 482 : 472, tapCount = round < 3 ? 492 : 472, life = round < 3 ? 1000 : 0 };
                var request = MultiLivePerformance.Result([11, 12], new Dictionary<long, UserMultiLiveClearScoreRequest>
                    { [11] = MultiLivePerformance.Score(ownerScore, 11, 72), [12] = MultiLivePerformance.Score(guestScore, 12, 72) });
                using var member = new DiarkisUdpClient(JsonSerializer.SerializeToNode(await Auth(12), DumpJson.Options)!.AsObject(), true);
                await Connect(member);
                var room = await Open(member);
                using var ownerHttp = Client(11);
                using var guestHttp = Client(12);
                var before = new[] { store.Read(11)!, store.Read(12)! };
                var output = Path.Combine(masterPath, "life-zero-" + round);
                Directory.CreateDirectory(output);
                var report = new JsonObject();
                check(await MultiLiveFlow.Run(owner, member, ownerHttp, guestHttp, before[0].Data, before[1].Data,
                    room.Owner, room.Guest, room.Seen, 1, song, output, report, cancel,
                    new MultiLivePerformance { DurationMilliseconds = 1, OwnerScore = ownerScore, GuestScore = guestScore })
                    && report["phase"]!.GetValue<string>() == "cleared", "生命归零和正常成员从Normal开局贯通双边结算");
                var liveId = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "live-state.local.json")))!["liveId"]!.GetValue<string>();
                await Leave(member, "12");
                await Response(owner, 1, 102, 255);
                await Leave(owner, "11");
                await Lookup(numbered.roomNo, 404);
                foreach (var (id, prior, response) in new[] { (11, before[0], ownerHttp.LastResponse!), (12, before[1], guestHttp.LastResponse!) })
                {
                    var point = JsonSerializer.Deserialize<UserLivePoint>(response["userLivePoint"]!.ToJsonString(), DumpJson.Options)!;
                    var rewards = JsonSerializer.Deserialize<UserResource[]>(response["scoreRankRewards"]!.ToJsonString(), DumpJson.Options)!;
                    check(point.addNormalProgress == 1 && point.addDailyBonusProgress == (round < 3 ? 10 : 0)
                        && point.livePointBonusRemaining == Math.Max(0, 2 - round)
                        && response["user1"]!["mvpFlg"]?.GetValue<bool>() == !guestMvp
                        && response["user2"]!["mvpFlg"]?.GetValue<bool>() == guestMvp,
                        "生命归零不取消每日加成或最高分MVP，三次名额逐场扣除");
                    using var independent = Client(id);
                    var suite = JsonSerializer.Deserialize<SuiteUser>((await independent.Suite()).ToJsonString(), DumpJson.Options)!;
                    var record = suite.userMusicResults.Single(r => r.playType == "multi" && r.musicDifficultyType == "normal");
                    var previousRecord = prior.Data.userMusicResults.SingleOrDefault(r => r.playType == "multi" && r.musicDifficultyType == "normal");
                    var mvp = id == 11 ? !guestMvp : guestMvp;
                    check(suite.userGamedata.totalExp - prior.Data.userGamedata.totalExp == 200
                        && suite.userGamedata.coin - prior.Data.userGamedata.coin == 200
                        && suite.userCards.Single(c => c.cardId == 1).totalExp == Math.Min(1000, 240 + (round + 1) * 600)
                        && suite.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress
                            - prior.Data.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress == (round < 3 ? 11 : 1)
                        && suite.userPracticeTickets.Single(t => t.practiceTicketId == 1).quantity
                            - prior.Data.userPracticeTickets.Single(t => t.practiceTicketId == 1).quantity == 2,
                        "独立Suite确认生命归零仍有经验、卡牌、金币、练习券和免费任务进度");
                    check(record.playResult == "clear" && record.highScore == Math.Max(previousRecord?.highScore ?? 0, id == 11 ? ownerScore.score : guestScore.score)
                        && record.mvpCount == (previousRecord?.mvpCount ?? 0) + (mvp ? 1 : 0)
                        && suite.userHonorMissions.Single(m => m.honorMissionType == "clear_live").progress
                            - prior.Data.userHonorMissions.Single(m => m.honorMissionType == "clear_live").progress == 1
                        && suite.userHonorMissions.All(m => m.honorMissionType != "finish_live_with_empty_life"),
                        "首次多人Normal成绩记为通关并累计MVP及通关任务，不增加单人空生命任务");
                    check(response["userMusicAchievements"]!.AsArray().Count == (round == 0 ? 3 : 0)
                        && suite.userMusicAchievements.Select(a => a.musicAchievementId).Order().SequenceEqual([9, 10, 11])
                        && suite.userChargedCurrency.free - prior.Data.userChargedCurrency.free == (round == 0 ? 15 : 0)
                        && suite.userChargedCurrency.paid == prior.Data.userChargedCurrency.paid,
                        "生命归零仍取得Normal连击成就与对应奖励，再次演出不重复发放");
                    check(suite.userCharacterLiveUsageCounts.All(c => c.usageCount == prior.Data.userCharacterLiveUsageCounts
                        .Single(p => p.characterId == c.characterId && p.characterLiveUsageType == c.characterLiveUsageType).usageCount + 1)
                        && suite.userHonorMissions.Single(m => m.honorMissionType == "multi_live_mvp").progress
                            - prior.Data.userHonorMissions.Single(m => m.honorMissionType == "multi_live_mvp").progress
                            == (mvp ? 1 : 0)
                        && suite.userHonorMissions.Single(m => m.honorMissionType == "collect_coin_total").progress
                            - prior.Data.userHonorMissions.Single(m => m.honorMissionType == "collect_coin_total").progress == 200,
                        "生命归零仍累计角色使用、金币任务和MVP任务，MVP只归最高分成员");
                    var grants = rewards.Where(r => r.resourceType == "material").GroupBy(r => r.resourceId)
                        .ToDictionary(g => g.Key, g => g.Sum(r => r.quantity));
                    (int Id, int Quantity)[] expectedMaterials = round switch
                    {
                        0 => [(5, 2), (3, 1), (7, 1)],
                        1 => [(4, 2), (4, 1), (10, 1)],
                        2 => [(2, 2), (5, 1), (9, 1)],
                        3 => [(1, 2), (5, 1), (10, 1)],
                        4 => [(4, 2), (5, 1), (9, 1)],
                        _ => [(1, 2), (3, 1), (10, 1)]
                    };
                    check(rewards.Where(r => r.resourceType == "material").Select(r => (r.resourceId, r.quantity)).SequenceEqual(expectedMaterials)
                        && grants.All(p => suite.userMaterials.Single(m => m.materialId == p.Key).quantity
                        - (prior.Data.userMaterials.SingleOrDefault(m => m.materialId == p.Key)?.quantity ?? 0) == p.Value)
                        && (round != 1 || rewards.Count(r => r.resourceType == "material" && r.resourceId == 4) == 2 && grants[4] == 3),
                        "C档额外样本逐项入账，同一材料分项2加1保持响应并合计为3");
                    var settled = DumpSerializer.Serialize(store.Read(id)!.Data);
                    await Request(id, HttpMethod.Put, $"/api/user/{id}/multi-live/{liveId}", DumpSerializer.Serialize(request), 404);
                    check(DumpSerializer.Serialize(store.Read(id)!.Data).AsSpan().SequenceEqual(settled), "生命归零场次重复结算不改变任何用户状态");
                }
                if (round == 0)
                {
                    var state = store.Read(11)!;
                    state.Private.MultiLiveSessions.Add(liveId, new() { Master = MultiLiveFlow.PrepareRequest(state.Data, roomId, 1, song, [11, 12]),
                        Start = MultiLiveFlow.MemberRequest(state.Data, roomId, 1, song), CreatedAt = clock.GetUtcNow().ToUnixTimeMilliseconds() });
                    store.Save(11, state);
                    var snapshot = DumpSerializer.Serialize(state.Data);
                    request.score1.life = -1;
                    await Request(11, HttpMethod.Put, $"/api/user/11/multi-live/{liveId}", DumpSerializer.Serialize(request), 400);
                    request.score1.life = 0;
                    check(DumpSerializer.Serialize(store.Read(11)!.Data).AsSpan().SequenceEqual(snapshot)
                        && store.Read(11)!.Private.MultiLiveSessions.ContainsKey(liveId)
                        && store.Read(11)!.Private.DailyLivePoint.BonusCount == 1,
                        "负生命值拒绝后保留完整状态、会话及每日次数");
                    state.Private.MultiLiveSessions.Clear();
                    store.Save(11, state);
                }
            }
            var database = Path.Combine(masterPath, "life-zero.db");
            using (var saved = new SqliteUserStore(database))
                foreach (var id in new[] { 11, 12 }) saved.Save(id, store.Read(id)!);
            using (var reopened = new SqliteUserStore(database))
                foreach (var id in new[] { 11, 12 })
                    check(reopened.Read(id)!.Data.userMusicResults.Single(r => r.playType == "multi" && r.musicDifficultyType == "normal").playResult == "clear"
                        && reopened.Read(id)!.Private.DailyLivePoint.BonusCount == 3 && reopened.Read(id)!.Private.MultiLiveSessions.Count == 0,
                        "SQLite重启保留生命归零的多人通关记录和已消费的每日次数及会话");
        }

        async Task PlayMixedDifficulty()
        {
            foreach (var id in new[] { 11, 12 })
            {
                var state = store.Read(id)!;
                state.Data.userMusicResults =
                [
                    new() { musicId = 7, musicDifficultyType = "easy", playType = "solo", highScore = 900000,
                        playResult = id == 11 ? "full_perfect" : "clear", fullComboFlg = id == 11, fullPerfectFlg = id == 11 },
                    new() { musicId = 7, musicDifficultyType = "normal", playType = "solo", highScore = 800000, playResult = "clear" }
                ];
                state.Data.userMusicAchievements = id == 11 ? [new() { musicId = 7, musicAchievementId = 8 }] : [];
                state.Data.userHonorMissions = state.Data.userHonorMissions.Where(m => m.honorMissionType is not ("easy_full_combo" or "normal_full_combo")).ToArray();
                if (id == 11) state.Data.userHonorMissions = [.. state.Data.userHonorMissions, new() { honorMissionType = "easy_full_combo", progress = 1, achievedMissionIds = [] }];
                if (id == 11) state.Data.userHonorMissions = [.. state.Data.userHonorMissions, new() { honorMissionType = "play_level_full_combo_clear", progress = 5, achievedMissionIds = [] }];
                foreach (var card in state.Data.userCards) { card.totalExp = card.exp = 0; card.level = 1; }
                state.Private.DailyLivePoint.BonusCount = 0;
                store.Save(id, state);
            }
            var rejected = false;
            try { MultiLiveSong.Read(masterPath, 7, "hard", 1); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "master中不存在的难度在业务请求前明确拒绝");
            for (var round = 0; round < 4; round++)
            {
                var ownerSong = MultiLiveSong.Read(masterPath, 7, round % 2 == 0 ? "easy" : "normal", 1);
                var guestSong = MultiLiveSong.Read(masterPath, 7, round % 2 == 0 ? "normal" : "easy", 1);
                var ownerScore = MixedScore(ownerSong, 100000, round == 3, round == 3);
                var guestScore = MixedScore(guestSong, 150000, true, round >= 2);
                var performance = new MultiLivePerformance { DurationMilliseconds = 1, GuestDifficulty = guestSong.Difficulty,
                    OwnerBoostCount = round == 3 ? 1 : 0, GuestBoostCount = round == 3 ? 2 : 0, OwnerScore = ownerScore, GuestScore = guestScore };
                var result = MultiLivePerformance.Result([11, 12], new Dictionary<long, UserMultiLiveClearScoreRequest>
                    { [11] = MultiLivePerformance.Score(ownerScore, 11, ownerSong.DifficultyId), [12] = MultiLivePerformance.Score(guestScore, 12, guestSong.DifficultyId) });
                rewardRandom.Choice = 0;
                using var member = new DiarkisUdpClient(JsonSerializer.SerializeToNode(await Auth(12), DumpJson.Options)!.AsObject(), true);
                await Connect(member);
                var room = await Open(member);
                using var ownerHttp = Client(11);
                using var guestHttp = Client(12);
                var before = new[] { store.Read(11)!, store.Read(12)! };
                var output = Path.Combine(masterPath, "mixed-" + round);
                Directory.CreateDirectory(output);
                if (round == 0)
                {
                    foreach (var wrong in new[] { guestSong with { MusicId = 8 }, guestSong with { VocalId = 2 } })
                    {
                        rejected = false;
                        try { await MultiLiveFlow.Run(owner, member, ownerHttp, guestHttp, before[0].Data, before[1].Data,
                            room.Owner, room.Guest, room.Seen, 1, ownerSong, output, new(), cancel, performance, wrong); }
                        catch (InvalidDataException) { rejected = true; }
                        check(rejected && store.Read(11)!.Private.MultiLiveSessions.Count == 0 && store.Read(12)!.Private.MultiLiveSessions.Count == 0,
                            "不同歌曲或版本在同步及HTTP准备前拒绝，不占用本场会话");
                    }
                    guestScore.musicDifficultyId = ownerSong.DifficultyId;
                    rejected = false;
                    try { await MultiLiveFlow.Run(owner, member, ownerHttp, guestHttp, before[0].Data, before[1].Data,
                        room.Owner, room.Guest, room.Seen, 1, ownerSong, output, new(), cancel, performance, guestSong); }
                    catch (InvalidDataException) { rejected = true; }
                    check(rejected, "成员成绩模板不能冒用房主谱面ID");
                    guestScore.musicDifficultyId = 0;
                }
                var report = new JsonObject();
                check(await MultiLiveFlow.Run(owner, member, ownerHttp, guestHttp, before[0].Data, before[1].Data,
                    room.Owner, room.Guest, room.Seen, 1, ownerSong, output, report, cancel, performance, guestSong)
                    && report["phase"]!.GetValue<string>() == "cleared", "双方不同难度贯通实际Server准备、成绩同步及双边结算");
                var journal = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "live-state.local.json")))!;
                var liveId = journal["liveId"]!.GetValue<string>();
                check(journal["difficultyId"]!.GetValue<int>() == ownerSong.DifficultyId && journal["guestDifficultyId"]!.GetValue<int>() == guestSong.DifficultyId,
                    "恢复记录分别保留双方实际谱面ID");
                await Leave(member, "12");
                await Response(owner, 1, 102, 255);
                await Leave(owner, "11");
                await Lookup(numbered.roomNo, 404);
                foreach (var (id, prior, song, score, response) in new[] { (11, before[0], ownerSong, ownerScore, ownerHttp.LastResponse!), (12, before[1], guestSong, guestScore, guestHttp.LastResponse!) })
                {
                    var rate = round == 3 ? (id == 11 ? 5 : 10) : 1;
                    var fullCombo = score.goodCount == 0;
                    var fullPerfect = fullCombo && score.greatCount == 0;
                    check(response["user1"]!["musicDifficultyId"]!.GetValue<int>() == ownerSong.DifficultyId
                        && response["user2"]!["musicDifficultyId"]!.GetValue<int>() == guestSong.DifficultyId
                        && response["user" + (id == 11 ? 1 : 2)]!["fullComboFlg"]?.GetValue<bool>() == fullCombo
                        && response["user" + (id == 11 ? 1 : 2)]!["fullPerfectFlg"]?.GetValue<bool>() == fullPerfect
                        && response["scoreRank"]!.GetValue<string>() == "rank_c" && response["multiScoreRank"]!.GetValue<string>() == "rank_c",
                        "每槽返回各自谱面和FC/AP判定，评分使用同曲的多人参考谱面");
                    using var independent = Client(id);
                    var suite = JsonSerializer.Deserialize<SuiteUser>((await independent.Suite()).ToJsonString(), DumpJson.Options)!;
                    var record = suite.userMusicResults.Single(r => r.playType == "multi" && r.musicDifficultyType == song.Difficulty);
                    var previousRecord = prior.Data.userMusicResults.SingleOrDefault(r => r.playType == "multi" && r.musicDifficultyType == song.Difficulty);
                    check(record.highScore == Math.Max(previousRecord?.highScore ?? 0, score.score)
                        && record.fullComboFlg == (fullCombo || previousRecord?.fullComboFlg == true)
                        && record.fullPerfectFlg == (fullPerfect || previousRecord?.fullPerfectFlg == true)
                        && record.playResult == (record.fullPerfectFlg ? "full_perfect" : record.fullComboFlg ? "full_combo" : "clear"),
                        "多人各难度分别保存最高分和FC/AP，不继承单人更高分或判定");
                    foreach (var old in prior.Data.userMusicResults.Where(r => r.playType == "solo" || r.musicDifficultyType != song.Difficulty))
                        check(DumpSerializer.Serialize(old).AsSpan().SequenceEqual(DumpSerializer.Serialize(suite.userMusicResults.Single(r => r.playType == old.playType && r.musicDifficultyType == old.musicDifficultyType))),
                            "本次不改变其他难度及单人记录");
                    var candidates = song.Difficulty == "normal" ? (fullCombo ? new[] { 9, 10, 11, 12 } : [9, 10, 11]) : (fullCombo ? [8] : Array.Empty<int>());
                    var newly = candidates.Except(prior.Data.userMusicAchievements.Select(a => a.musicAchievementId)).Order().ToArray();
                    var coinBonus = newly.Count(a => a is 8 or 12) * 5;
                    check(response["userMusicAchievements"]!.AsArray().Select(a => a!["musicAchievementId"]!.GetValue<int>()).Order().SequenceEqual(newly)
                        && suite.userChargedCurrency.free - prior.Data.userChargedCurrency.free == newly.Length * 5 - coinBonus
                        && suite.userChargedCurrency.paid == prior.Data.userChargedCurrency.paid,
                        "按本人谱面音符数解锁连击成就，新成就只发一次且不乘体力倍率");
                    var firstFc = fullCombo && !prior.Data.userMusicResults.Any(r => r.musicDifficultyType == song.Difficulty && r.fullComboFlg);
                    var type = song.Difficulty + "_full_combo";
                    check(Honor(suite, type) - Honor(prior.Data, type) == (firstFc ? 1 : 0)
                        && Honor(suite, (song.Difficulty == "normal" ? "easy" : "normal") + "_full_combo")
                            == Honor(prior.Data, (song.Difficulty == "normal" ? "easy" : "normal") + "_full_combo"),
                        "首次全连只增加对应难度曲数，重复全连或已有单人全连不重复计数");
                    check(Honor(suite, "play_level_full_combo_clear") == Math.Max(Honor(prior.Data, "play_level_full_combo_clear"), fullCombo ? (song.Difficulty == "normal" ? 10 : 5) : 0),
                        "最高全连等级只取本人谱面，简单难度全连不会降低已有最高等级");
                    var hints = response["updatedResources"]!["userHonorMissions"]!.AsArray()
                        .Where(m => m!["honorMissionType"]!.GetValue<string>() == "normal_full_combo")
                        .SelectMany(m => m!["achievedMissionIds"]!.AsArray()).Select(v => v!.GetValue<int>()).ToArray();
                    check(hints.SequenceEqual(firstFc && song.Difficulty == "normal" ? [777] : Array.Empty<int>())
                        && suite.userHonorMissions.All(m => m.achievedMissionIds == null || m.achievedMissionIds.Length == 0),
                        "首次Normal全连返回本次达成提示，提示不持久化且重复演出不再返回");
                    check(suite.userGamedata.totalExp - prior.Data.userGamedata.totalExp == 200 * rate
                        && suite.userGamedata.coin - prior.Data.userGamedata.coin == 200 * rate + coinBonus
                        && suite.userCards.All(c => c.totalExp == Math.Min(1000, prior.Data.userCards.Single(p => p.cardId == c.cardId).totalExp + 600 * rate))
                        && suite.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress
                            - prior.Data.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress == rate + (round < 3 ? 10 : 0),
                        "不同难度与个人体力组合仍正确结算经验、卡牌、金币及每日P");
                    check(Honor(suite, "collect_coin_total") - Honor(prior.Data, "collect_coin_total") == 200 * rate + coinBonus
                        && Honor(suite, "clear_multi_live_full_combo_all") == Honor(prior.Data, "clear_multi_live_full_combo_all"),
                        "成就金币不乘倍率且计入金币任务，本不同难度双人用例不增加全员全连任务");
                    var snapshot = DumpSerializer.Serialize(store.Read(id)!.Data);
                    await Request(id, HttpMethod.Put, $"/api/user/{id}/multi-live/{liveId}", DumpSerializer.Serialize(result), 404);
                    check(DumpSerializer.Serialize(store.Read(id)!.Data).AsSpan().SequenceEqual(snapshot), "不同难度场次重复结算不再改变任何状态");
                }
                if (round == 0)
                {
                    var settled = store.Read(12)!;
                    var pending = before[1].DeepClone();
                    pending.Private.MultiLiveSessions.Add(liveId, new()
                    {
                        Master = MultiLiveFlow.PrepareRequest(before[0].Data, roomId, 1, ownerSong, [11, 12]),
                        Start = MultiLiveFlow.MemberRequest(pending.Data, roomId, 1, guestSong), CreatedAt = clock.GetUtcNow().ToUnixTimeMilliseconds()
                    });
                    store.Save(12, pending);
                    var snapshot = DumpSerializer.Serialize(pending.Data);
                    result.score2.musicDifficultyId = ownerSong.DifficultyId;
                    await Request(12, HttpMethod.Put, $"/api/user/12/multi-live/{liveId}", DumpSerializer.Serialize(result), 400);
                    result.score2.musicDifficultyId = guestSong.DifficultyId;
                    using var scope = app.Services.CreateScope();
                    var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
                    var service = scope.ServiceProvider.GetRequiredService<MultiLiveService>();
                    var failed = false;
                    try { operations.Execute(12, () => { service.Clear(liveId, result); return new BrokenResponse(); }); }
                    catch (MessagePack.MessagePackSerializationException) { failed = true; }
                    check(failed && DumpSerializer.Serialize(store.Read(12)!.Data).AsSpan().SequenceEqual(snapshot)
                        && store.Read(12)!.Private.MultiLiveSessions.ContainsKey(liveId) && store.Read(12)!.Private.DailyLivePoint.BonusCount == 0,
                        "成员错谱拒绝或编码失败不留下首次全连、成就、奖励或每日次数，也不删除会话");
                    store.Save(12, settled);
                }
            }
            var database = Path.Combine(masterPath, "mixed.db");
            using (var saved = new SqliteUserStore(database))
                foreach (var id in new[] { 11, 12 }) saved.Save(id, store.Read(id)!);
            using (var reopened = new SqliteUserStore(database))
                foreach (var id in new[] { 11, 12 })
                {
                    var restored = reopened.Read(id)!;
                    var expected = store.Read(id)!.Data;
                    expected.now = restored.Data.now;
                    expected.refreshableTypes = restored.Data.refreshableTypes;
                    check(DumpSerializer.Serialize(restored.Data).AsSpan().SequenceEqual(DumpSerializer.Serialize(expected))
                        && restored.Private.MultiLiveSessions.Count == 0 && restored.Private.DailyLivePoint.BonusCount == 3,
                        "SQLite重启保留双方分难度的完整成绩、成就和首次全连任务");
                }
            static int Honor(SuiteUser data, string type) => data.userHonorMissions.SingleOrDefault(m => m.honorMissionType == type)?.progress ?? 0;
            static UserMultiLiveClearScoreRequest MixedScore(MultiLiveSong song, int score, bool fc, bool ap)
            {
                var notes = song.Difficulty == "normal" ? 492 : 220;
                return new() { score = score, perfectCount = ap ? notes : notes - 10, greatCount = fc && !ap ? 10 : 0,
                    goodCount = fc ? 0 : 10, maxCombo = fc ? notes : notes - 10, tapCount = notes, life = 1000 };
            }
        }

        async Task PlayReplay()
        {
            foreach (var id in new[] { 11, 12 })
            {
                var state = store.Read(id)!;
                state.Data.userGamedata.deck = 1;
                state.Data.userGamedata.rank = 1;
                state.Data.userGamedata.exp = state.Data.userGamedata.totalExp = 0;
                state.Data.userBoost = new() { current = 10, recoveryAt = (ulong)clock.GetUtcNow().ToUnixTimeMilliseconds() };
                var card = state.Data.userCards.Single(c => c.cardId == 1);
                card.level = 1;
                card.exp = card.totalExp = 0;
                state.Private.DailyLivePoint.BonusCount = 0;
                store.Save(id, state);
            }
            var initial = new[] { store.Read(11)!, store.Read(12)! };
            using var member = new DiarkisUdpClient(JsonSerializer.SerializeToNode(await Auth(12), DumpJson.Options)!.AsObject(), true);
            await Connect(member);
            var room = await Open(member);
            var firstRoomId = roomId;
            var firstNumber = numbered.roomNo;
            using var ownerHttp = Client(11);
            using var guestHttp = Client(12);
            var liveIds = new HashSet<string>();
            string? lastLiveId = null;
            for (var round = 0; round < 2; round++)
            {
                var song = round == 0 ? new MultiLiveSong(7, 71, 1, "easy") : new MultiLiveSong(8, 82, 2, "normal");
                var guestSong = round == 0 ? song : new MultiLiveSong(8, 81, 2, "easy");
                var ownerScore = new UserMultiLiveClearScoreRequest { score = 100000, perfectCount = round == 0 ? 210 : 482,
                    goodCount = 10, maxCombo = round == 0 ? 210 : 482, tapCount = round == 0 ? 220 : 492, life = 1000 };
                var guestScore = new UserMultiLiveClearScoreRequest { score = 100000, perfectCount = 210,
                    goodCount = 10, maxCombo = 210, tapCount = 220, life = 1000 };
                var before = new[] { store.Read(11)!, store.Read(12)! };
                var output = Path.Combine(masterPath, "replay-" + round);
                Directory.CreateDirectory(output);
                var report = new JsonObject();
                rewardRandom.Choice = round;
                check(await MultiLiveFlow.Run(owner, member, ownerHttp, guestHttp, before[0].Data, before[1].Data,
                    room.Owner, room.Guest, room.Seen, 1, song, output, report, cancel,
                    new MultiLivePerformance { DurationMilliseconds = 1, OwnerScore = ownerScore, GuestScore = guestScore,
                        GuestDifficulty = "easy" }, guestSong), "同一私房连续两场贯通真实HTTP和UDP，第二场更换歌曲及双方难度");
                var liveId = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "live-state.local.json")))!["liveId"]!.GetValue<string>();
                lastLiveId = liveId;
                check(liveIds.Add(liveId) && liveIds.Count == round + 1 && roomId == firstRoomId && numbered.roomNo == firstNumber,
                    "再演保留房间和房号，每场申请不同Live ID");
                foreach (var (id, prior, response, difficulty) in new[]
                    { (11, before[0], ownerHttp.LastResponse!, song.Difficulty), (12, before[1], guestHttp.LastResponse!, guestSong.Difficulty) })
                {
                    using var independent = Client(id);
                    var suite = JsonSerializer.Deserialize<SuiteUser>((await independent.Suite()).ToJsonString(), DumpJson.Options)!;
                    check(suite.userGamedata.totalExp == (round + 1) * 20
                        && suite.userGamedata.coin - initial[id - 11].Data.userGamedata.coin == (round + 1) * 20
                        && suite.userCards.Single(c => c.cardId == 1).totalExp == (round + 1) * 60
                        && suite.userBoost.current == 10 && store.Read(id)!.Private.DailyLivePoint.BonusCount == round + 1,
                        "连续演出的经验、卡牌、金币、每日次数及体力独立入账");
                    check(response["multiScoreRank"]!.GetValue<string>() == "rank_d"
                        && response["userLivePoint"]!["addNormalProgress"]!.GetValue<int>() == 1
                        && response["userLivePoint"]!["addDailyBonusProgress"]!.GetValue<int>() == 10
                        && response["userLivePoint"]!["livePointBonusRemaining"]!.GetValue<int>() == 2 - round
                        && suite.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress
                            - initial[id - 11].Data.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress == (round + 1) * 11
                        && suite.userMusicResults.Any(r => r.musicId == song.MusicId && r.musicDifficultyType == difficulty && r.playType == "multi"),
                        "再演按新歌曲和各自谱面保存成绩，每场普通P和每日P各入账一次");
                    check(suite.userChargedCurrency.free - initial[id - 11].Data.userChargedCurrency.free == (round == 1 && id == 11 ? 15 : 0)
                        && suite.userChargedCurrency.paid == initial[id - 11].Data.userChargedCurrency.paid
                        && suite.userHonorMissions.Single(m => m.honorMissionType == "collect_coin_total").progress
                            - initial[id - 11].Data.userHonorMissions.Single(m => m.honorMissionType == "collect_coin_total").progress == (round + 1) * 20
                        && DumpSerializer.Serialize(suite.userPracticeTickets).AsSpan().SequenceEqual(DumpSerializer.Serialize(initial[id - 11].Data.userPracticeTickets)),
                        "新曲Normal连击成就仅给本人15宝石，金币任务逐场累计，付费货币与练习券不变");
                    foreach (var reward in JsonSerializer.Deserialize<UserResource[]>(response["scoreRankRewards"]!.ToJsonString(), DumpJson.Options)!
                        .Where(r => r.resourceType == "material"))
                        check(suite.userMaterials.Single(m => m.materialId == reward.resourceId).quantity
                            - (prior.Data.userMaterials.SingleOrDefault(m => m.materialId == reward.resourceId)?.quantity ?? 0) == reward.quantity,
                            "每场再演的材料响应与实际库存一致");
                    var snapshot = DumpSerializer.Serialize(store.Read(id)!.Data);
                    foreach (var used in liveIds)
                        await Request(id, HttpMethod.Put, $"/api/user/{id}/multi-live/{used}", DumpSerializer.Serialize(
                            MultiLivePerformance.Result([11, 12], new Dictionary<long, UserMultiLiveClearScoreRequest>
                                { [11] = MultiLivePerformance.Score(ownerScore, 11, song.DifficultyId),
                                    [12] = MultiLivePerformance.Score(guestScore, 12, guestSong.DifficultyId) })), 404);
                    check(DumpSerializer.Serialize(store.Read(id)!.Data).AsSpan().SequenceEqual(snapshot),
                        "再演后旧场和本场重复结算均404，不影响新场资源或成绩");
                }
                if (round == 0)
                {
                    var snapshots = new[] { DumpSerializer.Serialize(store.Read(11)!.Data), DumpSerializer.Serialize(store.Read(12)!.Data) };
                    var recreate = new JsonObject();
                    await MultiLiveFlow.Recreate(owner, member, room.Owner, room.Guest, output, recreate, cancel);
                    check(recreate["transportAcknowledged"]!.GetValue<bool>()
                        && DumpSerializer.Serialize(store.Read(11)!.Data).AsSpan().SequenceEqual(snapshots[0])
                        && DumpSerializer.Serialize(store.Read(12)!.Data).AsSpan().SequenceEqual(snapshots[1]),
                        "再演只改变房间准备状态，可靠发送完成且不改用户资源或发奖");
                    var matching = MultiLiveFlow.Property((2, SyncPropertyEncoding.Int32(1)), (3, SyncPropertyEncoding.Int32(1)));
                    await owner.SendReliableAsync(2, 10010, DumpSerializer.Serialize(matching), cancel);
                    foreach (var connection in new[] { owner, member })
                        check(MultiLiveFlow.ReadRoom(await Response(connection, 2, 10010, 255)).Any(p => MultiLiveFlow.Matches(p, matching)),
                            "回房界面的STEP1和ATYPE1由房主同步到双方");
                    check(DumpSerializer.Deserialize<PrivateRoomNumberPayload>(await Lookup(firstNumber, 200)).roomId == firstRoomId,
                        "再演后原房号仍指向同一房间");
                }
            }
            await Leave(member, "12");
            await Response(owner, 1, 102, 255);
            await owner.SendReliableAsync(2, MultiLiveRoom.RecreateCommand, [], cancel);
            await owner.SendReliableAsync(2, MultiLiveFlow.PlayerPropertyCommand, DumpSerializer.Serialize(MultiLiveFlow.Status(game::Sekai.MultiLive.PlayerStatus.Matching)), cancel);
            await owner.FlushAsync(cancel);
            var observerBefore = DumpSerializer.Serialize(store.Read(13)!.Data);
            using var observer = new DiarkisUdpClient(JsonSerializer.SerializeToNode(await Auth(13), DumpJson.Options)!.AsObject(), true);
            await Connect(observer);
            await observer.SendReliableAsync(2, MultiLiveRoom.JoinCommand, MultiLiveRoom.JoinRequest(firstRoomId, "新成员", info), cancel);
            var joined = DumpSerializer.Deserialize<game::Sekai.MultiLive.MultiLiveRoomSyncData>((await Response(observer, 2, 3001)).Payload);
            var seen = MultiLiveRoom.ReadMemberJoins(await Response(owner, 2, 3001, 255));
            check(joined.roomId == firstRoomId && joined.privateRoomNumber == firstNumber && joined.ownerId == "11"
                && joined.players.Select(p => p.userId).ToHashSet().SetEquals(new[] { "11", "13" })
                && seen.Any(p => p.userId == "13") && DumpSerializer.Serialize(store.Read(13)!.Data).AsSpan().SequenceEqual(observerBefore),
                "第二场后再开放保留原房号，新成员可真实UDP入房且不获得旧场奖励");
            var previousOwner = joined.players.Single(p => p.userId == "11");
            check(joined.index != room.Guest.index && SyncPropertyEncoding.ReadInt32(joined.roomProperty.values[2]) == 7
                && SyncPropertyEncoding.ReadInt32(joined.roomProperty.values[4]) == 8
                && SyncPropertyEncoding.ReadString(joined.roomProperty.values[12]) == lastLiveId
                && SyncPropertyEncoding.ReadInt32(previousOwner.playerProperty.values[4]) == 1
                && SyncPropertyEncoding.ReadObject<UserMultiLiveClearScoreRequest>(previousOwner.playerProperty.values[10]).musicDifficultyId == 82,
                "再开放保留房间和成员同步字段，旧成绩不转给新成员，匹配状态由客户端覆盖");
            await Leave(observer, "13");
            await Response(owner, 1, 102, 255);
            await Leave(owner, "11");
            await Lookup(firstNumber, 404);
        }

        async Task PlayBoost()
        {
            var song = new MultiLiveSong(7, 72, 1, "normal");
            foreach (var id in new[] { 11, 12 })
            {
                var state = store.Read(id)!;
                state.Data.userGamedata.deck = 2;
                state.Data.userGamedata.rank = 1;
                state.Data.userGamedata.totalExp = state.Data.userGamedata.exp = 400;
                state.Data.userCards = [.. state.Data.userCards, .. Enumerable.Range(6, 5).Select(cardId => new UserCard { cardId = cardId, level = 1 })];
                state.Data.userDecks = [.. state.Data.userDecks, new() { deckId = 2, leader = 6, member1 = 6, member2 = 7, member3 = 8, member4 = 9, member5 = 10 }];
                state.Private.DailyLivePoint.BonusCount = 0;
                state.Data.userMusicAchievements = [];
                state.Data.userBoost = new() { current = 1, recoveryAt = (ulong)(clock.GetUtcNow().ToUnixTimeMilliseconds() - (id == 12 ? 1800000 : 0)) };
                store.Save(id, state);
            }
            var ownerScore = new UserMultiLiveClearScoreRequest { score = 100000, perfectCount = 482, goodCount = 10, maxCombo = 482, tapCount = 492, life = 1000 };
            var guestScore = new UserMultiLiveClearScoreRequest { score = 150000, perfectCount = 482, goodCount = 10, maxCombo = 482, tapCount = 492, life = 1000 };
            for (var round = 0; round < 7; round++)
            {
                var rankD = round >= 4;
                var baseExp = rankD ? 20 : 200;
                guestScore.score = rankD ? 100000 : 150000;
                var result = MultiLivePerformance.Result([11, 12], new Dictionary<long, UserMultiLiveClearScoreRequest>
                    { [11] = MultiLivePerformance.Score(ownerScore, 11, 72), [12] = MultiLivePerformance.Score(guestScore, 12, 72) });
                rewardRandom.Choice = 9 + round;
                var ownerCost = new[] { 1, 2, 6, 1, 1, 1, 0 }[round];
                var guestCost = round is 0 or 4 or 5 ? 2 : round == 6 ? 0 : 1;
                if (round == 2)
                {
                    var overCap = store.Read(11)!;
                    overCap.Data.userBoost = new() { current = 30, recoveryAt = (ulong)(clock.GetUtcNow().ToUnixTimeMilliseconds() - 1800000) };
                    store.Save(11, overCap);
                }
                if (round == 5)
                    foreach (var id in new[] { 11, 12 })
                    {
                        var state = store.Read(id)!;
                        state.Data.userBoost = new() { current = id == 11 ? 1 : 2, recoveryAt = (ulong)clock.GetUtcNow().ToUnixTimeMilliseconds() };
                        store.Save(id, state);
                    }
                if (round == 4)
                    foreach (var id in new[] { 11, 12 })
                    {
                        var state = store.Read(id)!;
                        var card = state.Data.userCards.Single(c => c.cardId == 6);
                        card.totalExp = card.exp = 0;
                        card.level = 1;
                        store.Save(id, state);
                    }
                using var member = new DiarkisUdpClient(JsonSerializer.SerializeToNode(await Auth(12), DumpJson.Options)!.AsObject(), true);
                await Connect(member);
                var room = await Open(member);
                using var ownerHttp = Client(11);
                using var guestHttp = Client(12);
                var before = new[] { store.Read(11)!, store.Read(12)! };
                var guestUser = JsonSerializer.Deserialize<SuiteUser>((await guestHttp.Suite()).ToJsonString(), DumpJson.Options)!;
                var output = Path.Combine(masterPath, "boost-" + round);
                Directory.CreateDirectory(output);
                var report = new JsonObject();
                check(await MultiLiveFlow.Run(owner, member, ownerHttp, guestHttp, before[0].Data, guestUser,
                    room.Owner, room.Guest, room.Seen, 1, song, output, report, cancel,
                    new MultiLivePerformance { DurationMilliseconds = 1, OwnerBoostCount = round >= 5 ? 6 : ownerCost,
                        GuestBoostCount = round >= 5 ? 6 : guestCost, OwnerScore = ownerScore, GuestScore = guestScore })
                    && report["phase"]!.GetValue<string>() == "cleared", "独立体力选择贯通双方真实HTTP准备、成绩交换和结算");
                var journal = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "live-state.local.json")))!;
                var liveId = journal["liveId"]!.GetValue<string>();
                check(journal["ownerBoostCount"]!.GetValue<int>() == ownerCost && journal["guestBoostCount"]!.GetValue<int>() == guestCost
                    && (round == 6 ? report["ownerPreparedBoost"] == null && report["guestPreparedBoost"] == null
                        : report["ownerPreparedBoost"]!["current"]!.GetValue<int>() == before[0].Data.userBoost.current
                            && report["guestPreparedBoost"]!["current"]!.GetValue<int>() == before[1].Data.userBoost.current + (round == 0 ? 1 : 0)),
                    "恢复记录保留双方实际档位；不足时各取剩余体力，用尽后以零体力继续");
                await Leave(member, "12");
                await Response(owner, 1, 102, 255);
                await Leave(owner, "11");
                await Lookup(numbered.roomNo, 404);
                foreach (var (id, prior, cost, response) in new[] { (11, before[0], ownerCost, ownerHttp.LastResponse!), (12, before[1], guestCost, guestHttp.LastResponse!) })
                {
                    var rate = cost == 0 ? 1 : cost == 6 ? 26 : cost * 5;
                    var boost = JsonSerializer.Deserialize<MasterBoost>(response["boost"]!.ToJsonString(), DumpJson.Options)!;
                    var exp = JsonSerializer.Deserialize<UpdateExpResult>(response["userExpResult"]!.ToJsonString(), DumpJson.Options)!;
                    var points = JsonSerializer.Deserialize<UserLivePoint>(response["userLivePoint"]!.ToJsonString(), DumpJson.Options)!;
                    var rewards = JsonSerializer.Deserialize<UserResource[]>(response["scoreRankRewards"]!.ToJsonString(), DumpJson.Options)!;
                    var rankRewards = JsonSerializer.Deserialize<UserResource[]>(response["playerRankRewards"]!.ToJsonString(), DumpJson.Options)!;
                    var cardExp = JsonSerializer.Deserialize<DeckCardUpdateExpResult[]>(response["deckCardExpResults"]!.ToJsonString(), DumpJson.Options)!;
                    check(boost.costBoost == cost && boost.expRate == rate && boost.rewardRate == rate && boost.livePointRate == rate
                        && exp.afterTotalExp - exp.beforeTotalExp == baseExp * rate
                        && cardExp.All(c => c.expResult.afterTotalExp == Math.Min(10000, c.expResult.beforeTotalExp + baseExp * 3 * rate)),
                        "各自读取master倍率，6体力使用26倍，卡牌经验遵守独立上限");
                    check(points.addNormalProgress == rate && points.addDailyBonusProgress == (round < 3 ? 10 : 0)
                        && points.livePointBonusRemaining == Math.Max(0, 2 - round)
                        && rewards.Single(r => r.resourceType == "coin").quantity == baseExp * rate
                        && (rewards.SingleOrDefault(r => r.resourceType == "practice_ticket")?.quantity ?? 0) == (rankD ? 0 : 2 * rate)
                        && response["multiScoreRank"]!.GetValue<string>() == (rankD ? "rank_d" : "rank_c"),
                        "评分奖励与普通P按个人倍率，每日10P固定且第四场耗尽");
                    (int Id, int Quantity)[] materials = round switch
                    {
                        0 => [(3, 2), (5, 1), (9, 1)],
                        1 => [(1, 2), (2, 1), (6, 1)],
                        2 => [(4, 2), (5, 1), (10, 1), (11, 2)],
                        3 => [(4, 2), (4, 1), (7, 1)],
                        4 => [(4, 2)],
                        5 => [(5, 2)],
                        _ => [(1, 2)]
                    };
                    check(rewards.Where(r => r.resourceType == "material").Select(r => (r.resourceId, r.quantity))
                        .SequenceEqual(materials.Select(m => (m.Id, m.Quantity * rate))), "新增C档材料及服装布料逐项乘倍率，相同材料分项保留");
                    using var independent = Client(id);
                    var suite = JsonSerializer.Deserialize<SuiteUser>((await independent.Suite()).ToJsonString(), DumpJson.Options)!;
                    var expectedBoost = prior.Data.userBoost.current + (id == 12 && round == 0 ? 1 : 0) - cost + (round == 0 ? 10 : 0);
                    check(suite.userBoost.current == expectedBoost && suite.userGamedata.totalExp == prior.Data.userGamedata.totalExp + baseExp * rate
                        && suite.userGamedata.rank == 2 && suite.userGamedata.coin - prior.Data.userGamedata.coin == baseExp * rate
                        && suite.userCards.Where(c => c.cardId >= 6).All(c => c.totalExp == Math.Min(10000,
                            prior.Data.userCards.Single(p => p.cardId == c.cardId).totalExp + baseExp * 3 * rate)),
                        "退出后独立Suite确认自然恢复、扣体力、升级补体力及经验仅生效一次");
                    check(suite.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress
                        - prior.Data.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress == rate + (round < 3 ? 10 : 0)
                        && suite.userChargedCurrency.free - prior.Data.userChargedCurrency.free == (round == 0 ? 40 : 0)
                        && suite.userChargedCurrency.paid == prior.Data.userChargedCurrency.paid
                        && rankRewards.Sum(r => r.quantity) == (round == 0 ? 25 : 0)
                        && response["userMusicAchievements"]!.AsArray().Count == (round == 0 ? 3 : 0)
                        && suite.userHonorMissions.Single(m => m.honorMissionType == "collect_coin_total").progress
                            - prior.Data.userHonorMissions.Single(m => m.honorMissionType == "collect_coin_total").progress == baseExp * rate,
                        "任务累计普通与每日P，等级及音乐成就奖励各发一次并且不乘体力倍率");
                    check(suite.userPracticeTickets.Single(t => t.practiceTicketId == 1).quantity
                        - prior.Data.userPracticeTickets.Single(t => t.practiceTicketId == 1).quantity == (rankD ? 0 : 2 * rate)
                        && response["user" + (id == 11 ? 1 : 2)]!["mvpFlg"]?.GetValue<bool>() == (rankD || id == 12),
                        "D档没有额外练习券，MVP只取原始分数且不受个人体力倍率影响");
                    foreach (var reward in rewards.GroupBy(r => (r.resourceType, r.resourceId)))
                    {
                        if (reward.Key.resourceType == "material")
                            check(suite.userMaterials.Single(m => m.materialId == reward.Key.resourceId).quantity
                                - (prior.Data.userMaterials.SingleOrDefault(m => m.materialId == reward.Key.resourceId)?.quantity ?? 0) == reward.Sum(r => r.quantity),
                                "材料含服装布料按实际合计入库");
                        if (reward.Key.resourceType == "practice_ticket")
                            check(suite.userPracticeTickets.Single(t => t.practiceTicketId == reward.Key.resourceId).quantity
                                - prior.Data.userPracticeTickets.Single(t => t.practiceTicketId == reward.Key.resourceId).quantity == reward.Sum(r => r.quantity), "练习券按倍率实际入库");
                    }
                    if (id == 11 && round == 2)
                        check(suite.userBoost.current == 24 && suite.userBoost.recoveryAt == (ulong)clock.GetUtcNow().ToUnixTimeMilliseconds(),
                            "持有超过自然上限的体力后消耗至上限内，恢复计时从本次结算开始");
                    var snapshot = DumpSerializer.Serialize(store.Read(id)!.Data);
                    await Request(id, HttpMethod.Put, $"/api/user/{id}/multi-live/{liveId}", DumpSerializer.Serialize(result), 404);
                    check(DumpSerializer.Serialize(store.Read(id)!.Data).AsSpan().SequenceEqual(snapshot), "非零体力场次重复提交不再次扣体力或发奖");
                }
                if (round == 0)
                {
                    var settled = store.Read(11)!;
                    var pending = before[0].DeepClone();
                    pending.Private.MultiLiveSessions.Add(liveId, new()
                    {
                        Master = MultiLiveFlow.PrepareRequest(pending.Data, roomId, 1, song, [11, 12], ownerCost),
                        Start = MultiLiveFlow.MemberRequest(pending.Data, roomId, 1, song, ownerCost), CreatedAt = clock.GetUtcNow().ToUnixTimeMilliseconds()
                    });
                    store.Save(11, pending);
                    using var scope = app.Services.CreateScope();
                    var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
                    var service = scope.ServiceProvider.GetRequiredService<MultiLiveService>();
                    var snapshot = DumpSerializer.Serialize(pending.Data);
                    var failed = false;
                    try { operations.Execute(11, () => { service.Clear(liveId, result); return new BrokenResponse(); }); }
                    catch (MessagePack.MessagePackSerializationException) { failed = true; }
                    check(failed && DumpSerializer.Serialize(store.Read(11)!.Data).AsSpan().SequenceEqual(snapshot)
                        && store.Read(11)!.Private.MultiLiveSessions.ContainsKey(liveId) && store.Read(11)!.Private.DailyLivePoint.BonusCount == 0,
                        "非零体力编码失败回滚扣除、升级恢复、全部奖励及每日次数，保留会话");
                    var overflow = pending.DeepClone();
                    overflow.Data.userPracticeTickets.Single(t => t.practiceTicketId == 1).quantity = int.MaxValue;
                    store.Save(11, overflow);
                    snapshot = DumpSerializer.Serialize(overflow.Data);
                    failed = false;
                    try { operations.Execute(11, () => service.Clear(liveId, result)); }
                    catch (OverflowException) { failed = true; }
                    check(failed && DumpSerializer.Serialize(store.Read(11)!.Data).AsSpan().SequenceEqual(snapshot)
                        && store.Read(11)!.Private.MultiLiveSessions.ContainsKey(liveId), "非零体力奖励溢出回滚此前体力、卡牌、等级及金币奖励");
                    pending.Private.MultiLiveSessions[liveId].BoostCount = 0;
                    pending.Private.MultiLiveSessions[liveId].Start.boostCount = 6;
                    pending.Data.userBoost.current = 1;
                    store.Save(11, pending);
                    snapshot = DumpSerializer.Serialize(pending.Data);
                    failed = false;
                    try { operations.Execute(11, () => { service.Clear(liveId, result); return new BrokenResponse(); }); }
                    catch (MessagePack.MessagePackSerializationException) { failed = true; }
                    check(failed && DumpSerializer.Serialize(store.Read(11)!.Data).AsSpan().SequenceEqual(snapshot)
                        && store.Read(11)!.Private.MultiLiveSessions.ContainsKey(liveId) && store.Read(11)!.Private.DailyLivePoint.BonusCount == 0,
                        "已准备零档位后的编码失败仍回滚全部奖励、升级恢复及每日次数");
                    var lowered = DumpSerializer.Deserialize<UserMultiLiveClearResponse>(await Request(11, HttpMethod.Put,
                        $"/api/user/11/multi-live/{liveId}", DumpSerializer.Serialize(result), 200));
                    check(lowered.boost.costBoost == 0 && lowered.userExpResult.afterTotalExp - lowered.userExpResult.beforeTotalExp == 200
                        && lowered.updatedResources.userBoost.current == 11 && lowered.userLivePoint.addNormalProgress == 1
                        && !store.Read(11)!.Private.MultiLiveSessions.ContainsKey(liveId),
                        "已准备零档位后体力增加也不升档，本场升级补体力仅进入剩余余额");
                    store.Save(11, settled);
                }
            }
            foreach (var id in new[] { 11, 12 })
            {
                var state = store.Read(id)!;
                state.Data.userBoost = new() { current = id == 11 ? 1 : 0,
                    recoveryAt = (ulong)(clock.GetUtcNow().ToUnixTimeMilliseconds() - 1800000 + 1) };
                store.Save(id, state);
            }
            using var rawMember = new DiarkisUdpClient(JsonSerializer.SerializeToNode(await Auth(12), DumpJson.Options)!.AsObject(), true);
            await Connect(rawMember);
            await Open(rawMember);
            var rawBefore = new[] { store.Read(11)!, store.Read(12)! };
            var rooms = app.Services.GetRequiredService<MultiLiveRooms>();
            rooms.Process(11, 2, 1013, [1]);
            rooms.Process(11, 2, 10010, DumpSerializer.Serialize(MultiLiveFlow.Property(
                (2, SyncPropertyEncoding.Int32(5)), (4, SyncPropertyEncoding.Int32(song.MusicId)))));
            foreach (var id in new long[] { 11, 12 })
                rooms.Process(id, 2, 10030, DumpSerializer.Serialize(MultiLiveFlow.Property(
                    (4, SyncPropertyEncoding.Int32(8)), (8, SyncPropertyEncoding.Int32(song.MusicId)), (7, SyncPropertyEncoding.String(song.Difficulty)))));
            var rawPrepare = DumpSerializer.Deserialize<MultiLiveResponse>(await Request(11, HttpMethod.Post, "/api/multi-live",
                DumpSerializer.Serialize(MultiLiveFlow.PrepareRequest(rawBefore[0].Data, roomId, 1, song, [11, 12], 2)), 200));
            var rawGuest = DumpSerializer.Deserialize<UserMultiLiveResponse>(await Request(12, HttpMethod.Post,
                $"/api/user/12/multi-live/{rawPrepare.liveId}", DumpSerializer.Serialize(MultiLiveFlow.MemberRequest(rawBefore[1].Data, roomId, 1, song, 2)), 200));
            check(rawPrepare.updatedResources.userBoost == null && rawGuest.updatedResources.userBoost == null
                && store.Read(11)!.Private.MultiLiveSessions[rawPrepare.liveId].Start.boostCount == 2
                && store.Read(12)!.Private.MultiLiveSessions[rawPrepare.liveId].Start.boostCount == 2
                && store.Read(11)!.Private.MultiLiveSessions[rawPrepare.liveId].BoostCount == 1
                && store.Read(12)!.Private.MultiLiveSessions[rawPrepare.liveId].BoostCount == 0
                && store.Read(11)!.Data.userBoost.current == 1 && store.Read(12)!.Data.userBoost.current == 0,
                "双方超额原始准备请求均成功，分别保存原始请求与实际1/0档位，不提前扣体力");
            var preparedDatabase = Path.Combine(masterPath, "prepared-boost.db");
            using (var saved = new SqliteUserStore(preparedDatabase))
                foreach (var id in new[] { 11, 12 }) saved.Save(id, store.Read(id)!);
            using (var reopened = new SqliteUserStore(preparedDatabase))
                foreach (var id in new[] { 11, 12 })
                    check(reopened.Read(id)!.Private.MultiLiveSessions[rawPrepare.liveId].BoostCount == (id == 11 ? 1 : 0)
                        && reopened.Read(id)!.Private.MultiLiveSessions[rawPrepare.liveId].Start.boostCount == 2,
                        "SQLite重启分别保留实际1/0档位与原始请求，零档位不回退到请求值");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            var rawResult = MultiLivePerformance.Result([11, 12], new Dictionary<long, UserMultiLiveClearScoreRequest>
                { [11] = MultiLivePerformance.Score(ownerScore, 11, 72), [12] = MultiLivePerformance.Score(guestScore, 12, 72) });
            var rawResponses = new UserMultiLiveClearResponse[2];
            for (var slot = 0; slot < 2; slot++)
                rawResponses[slot] = DumpSerializer.Deserialize<UserMultiLiveClearResponse>(await Request(11 + slot, HttpMethod.Put,
                    $"/api/user/{11 + slot}/multi-live/{rawPrepare.liveId}", DumpSerializer.Serialize(rawResult), 200));
            await Leave(rawMember, "12");
            await Response(owner, 1, 102, 255);
            await Leave(owner, "11");
            await Lookup(numbered.roomNo, 404);
            foreach (var (id, prior, response) in new[] { (11, rawBefore[0], rawResponses[0]), (12, rawBefore[1], rawResponses[1]) })
            {
                var rate = id == 11 ? 5 : 1;
                using var independent = Client(id);
                var suite = JsonSerializer.Deserialize<SuiteUser>((await independent.Suite()).ToJsonString(), DumpJson.Options)!;
                check(response.boost.costBoost == (id == 11 ? 1 : 0) && response.boost.expRate == rate
                    && response.boost.rewardRate == rate && response.boost.livePointRate == rate
                    && response.userExpResult.afterTotalExp - response.userExpResult.beforeTotalExp == 20 * rate
                    && suite.userGamedata.totalExp - prior.Data.userGamedata.totalExp == 20 * rate
                    && suite.userGamedata.coin - prior.Data.userGamedata.coin == 20 * rate && suite.userBoost.current == 1,
                    "超额请求按准备时1/0档位结算，演出中恢复的1体力保留，不提高本场倍率");
                check(response.userLivePoint.addNormalProgress == rate && response.userLivePoint.addDailyBonusProgress == 0
                    && suite.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress
                        - prior.Data.userLiveMissions.Single(m => m.liveMissionPeriodId == 1).progress == rate
                    && suite.userHonorMissions.Single(m => m.honorMissionType == "collect_coin_total").progress
                        - prior.Data.userHonorMissions.Single(m => m.honorMissionType == "collect_coin_total").progress == 20 * rate
                    && suite.userCards.Where(c => c.cardId >= 6).All(c => c.totalExp == Math.Min(10000,
                        prior.Data.userCards.Single(p => p.cardId == c.cardId).totalExp + 60 * rate)),
                    "不足体力的卡牌经验、普通P及金币任务按降档后倍率入账，每日奖励不重复");
                foreach (var reward in response.scoreRankRewards.Where(r => r.resourceType == "material"))
                    check(reward.quantity == 2 * rate && suite.userMaterials.Single(m => m.materialId == reward.resourceId).quantity
                        - (prior.Data.userMaterials.SingleOrDefault(m => m.materialId == reward.resourceId)?.quantity ?? 0) == reward.quantity,
                        "不足体力的材料响应与实际库存均按降档倍率发放");
                var snapshot = DumpSerializer.Serialize(store.Read(id)!.Data);
                await Request(id, HttpMethod.Put, $"/api/user/{id}/multi-live/{rawPrepare.liveId}", DumpSerializer.Serialize(rawResult), 404);
                check(DumpSerializer.Serialize(store.Read(id)!.Data).AsSpan().SequenceEqual(snapshot)
                    && store.Read(id)!.Private.MultiLiveSessions.Count == 0, "不足体力场次也只结算一次并消费会话");
            }
            var database = Path.Combine(masterPath, "boost.db");
            using (var saved = new SqliteUserStore(database))
                foreach (var id in new[] { 11, 12 }) saved.Save(id, store.Read(id)!);
            using (var reopened = new SqliteUserStore(database))
                foreach (var id in new[] { 11, 12 })
                {
                    var restored = reopened.Read(id)!;
                    var expected = store.Read(id)!.Data;
                    expected.now = restored.Data.now;
                    expected.refreshableTypes = restored.Data.refreshableTypes;
                    check(DumpSerializer.Serialize(restored.Data).AsSpan().SequenceEqual(DumpSerializer.Serialize(expected))
                        && restored.Private.DailyLivePoint.BonusCount == 3 && restored.Private.MultiLiveSessions.Count == 0,
                        "SQLite重启保留扣体力后的完整用户状态、每日次数和已消费会话");
                }
        }

        void CheckDailyBoundary()
        {
            using var scope = app.Services.CreateScope();
            var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var points = scope.ServiceProvider.GetRequiredService<LivePointService>();
            var boost = new MasterBoost { livePointRate = 1 };
            clock.Set(new DateTimeOffset(2026, 10, 20, 18, 59, 59, TimeSpan.Zero));
            var before = DumpSerializer.Deserialize<UserLivePoint>(operations.Execute(11, () => points.Build(boost, true)));
            check(before.addDailyBonusProgress == 0 && before.livePointBonusRemaining == 0, "JP日切前保留已耗尽的每日次数");
            clock.Advance(TimeSpan.FromSeconds(1));
            var solo = DumpSerializer.Deserialize<UserLivePoint>(operations.Execute(11, () => points.Build(boost)));
            check(solo.addDailyBonusProgress == 0 && solo.livePointBonusRemaining == 3
                && store.Read(11)!.Private.DailyLivePoint.BonusCount == 3, "JP凌晨4点单人查询显示新日名额，不发额外点或消费次数");
            var after = DumpSerializer.Deserialize<UserLivePoint>(operations.Execute(11, () => points.Build(boost, true)));
            check(after.addDailyBonusProgress == 10 && after.livePointBonusRemaining == 2
                && store.Read(11)!.Private.DailyLivePoint.BonusCount == 1, "新日首次多人通关重新发放一次额外点");
        }

        ProtocolClient Client(int id)
        {
            var file = $"realtime-{id}.json";
            JsonFiles.Write(Path.Combine(masterPath, file), new Dictionary<string, string> { ["X-Session-Token"] = JwtSignature.GenSessionToken(id) });
            return new(new TargetConfiguration { BaseUrl = app.Urls.Single(), UserId = id, HeadersFile = file },
                masterPath, ServerConfig.AesKey, ServerConfig.AesIv);
        }
        async Task Connect(DiarkisUdpClient connection)
        {
            await connection.ConnectAsync(cancel);
            await connection.FlushAsync(cancel);
            await connection.WaitForEchoAsync(1, cancel);
            check(connection.ClientKeyAcknowledged && connection.LastEcho?.IsOffline == false,
                "真实服务端完成SYN、clientKey确认及带原时间戳的Echo");
            var timestamp = new byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(timestamp, 1234567.0);
            await connection.SendUnreliableAsync(0, 3, timestamp, cancel);
            var ping = await Response(connection, 0, 3);
            check(ping.Payload.Length == 9 && ping.Payload.AsSpan(1).SequenceEqual(timestamp), "真实UDP Ping原样保留调用方时间戳");
        }
        async Task<UserDiarkisAuthResponse> Auth(int id) => DumpSerializer.Deserialize<UserDiarkisAuthResponse>(
            await Request(id, HttpMethod.Get, $"/api/user/{id}/diarkis-auth?diarkisServerType=multi", null, 200));
        async Task<game::Sekai.MultiLive.CreateMultiLivePrivateRoomResponse> Reserve(DiarkisUdpClient connection)
        {
            await connection.SendReliableAsync(2, 3060, MultiLiveReservation.CreateRequest(LiveRuleType.normal, 60, default, true, null, null), cancel);
            return MultiLiveReservation.ParseResponse(await Response(connection, 2, 3060));
        }
        async Task<PrivateRoomNumberPayload> Number(string id) => DumpSerializer.Deserialize<PrivateRoomNumberPayload>(
            await Request(11, HttpMethod.Post, $"/api/user/11/diarkis-room/{id}/no", NumberBody(), 200));
        Task<byte[]> Lookup(int number, int status) => Request(12, HttpMethod.Get, $"/api/user/12/diarkis-room-type/multi_live/no/{number}", null, status);
        async Task SyncNumber(DiarkisUdpClient connection, int number)
        {
            await connection.SendReliableAsync(2, 10010, MultiLiveRoom.RoomNumberRequest(number), cancel);
            check(MultiLiveRoom.ConfirmsRoomNumber(await Response(connection, 2, 10010, 255), number), "房号经真实属性推送确认");
        }
        async Task Leave(DiarkisUdpClient connection, string id)
        {
            await connection.SendReliableAsync(1, 102, MultiLiveRoom.LeaveRequest(roomId, id), cancel);
            check((await Response(connection, 1, 102)).Status == 1, "真实UDP退出成功响应");
        }
        async Task<DiarkisResponse> Response(DiarkisUdpClient connection, byte version, ushort command, byte status = 1)
        {
            while (true)
            {
                var response = await connection.ReceiveAsync(cancel);
                if (response.Version == version && response.Command == command && response.Status == status) return response;
            }
        }
        async Task<byte[]> Request(int user, HttpMethod method, string path, byte[]? body, int status)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Session-Token", JwtSignature.GenSessionToken(user));
            if (body != null)
            {
                request.Content = new ByteArrayContent(PrskCrypto.EncryptAesCbc(body));
                request.Content.Headers.ContentType = new("application/octet-stream");
            }
            using var response = await http.SendAsync(request, cancel);
            check((int)response.StatusCode == status, "房间HTTP实际状态符合当前操作");
            return PrskCrypto.DecryptAesCbc(await response.Content.ReadAsByteArrayAsync(cancel));
        }
        async Task DuplicateReservation()
        {
            var auth = await Auth(13);
            using var udp = new UdpClient();
            udp.Connect(IPAddress.Loopback, auth.udpPort);
            var sid = Convert.FromHexString(auth.sid);
            var key = Convert.FromHexString(auth.encryptionKey);
            var iv = Convert.FromHexString(auth.encryptionIv);
            var mac = Convert.FromHexString(auth.encryptionMacKey);
            await udp.SendAsync(DiarkisPacket.CreateUdp(2, 0, sid), cancel);
            await udp.ReceiveAsync(cancel);
            var clientKey = DiarkisPacket.CreateEncryptedRequest(0, 4, Encoding.UTF8.GetBytes(auth.clientKey), sid, key, iv, mac);
            await udp.SendAsync(DiarkisPacket.CreateUdp(3, 0, clientKey), cancel);
            await udp.ReceiveAsync(cancel);
            var bytes = MultiLiveReservation.CreateRequest(LiveRuleType.normal, 60, default, true, null, null);
            var original = DumpSerializer.Deserialize<game::Sekai.MultiLive.CreateMultiLivePrivateRoomData>(bytes);
            original.roomProperty.values[13] = SyncPropertyEncoding.String(new string('x', 2400));
            var fragments = DiarkisPacket.CreateReliableRequests(2, 3060, DumpSerializer.Serialize(original), sid, key, iv, mac, 0);
            for (var index = fragments.Count - 1; index >= 0; index--)
            {
                await udp.SendAsync(DiarkisPacket.CreateUdp(3, (uint)index + 1, fragments[index]), cancel);
                await udp.SendAsync(DiarkisPacket.CreateUdp(5, (uint)index + 1, fragments[index]), cancel);
            }
            DiarkisResponse reserved;
            while (true)
            {
                var datagram = DiarkisPacket.ParseUdp((await udp.ReceiveAsync(cancel)).Buffer);
                if (datagram.Flag is not (3 or 5)) continue;
                var encrypted = DiarkisPacket.ParseResponse(datagram.Payload);
                reserved = encrypted with { Payload = DiarkisEncryption.Decrypt(encrypted.Payload, key, iv, mac) };
                await udp.SendAsync(DiarkisPacket.CreateUdp(4, datagram.Sequence, sid), cancel);
                break;
            }
            var reservedId = MultiLiveReservation.ParseResponse(reserved).roomId;
            check(reservedId.Length == 52, "真实服务端对乱序、重复大分片仅执行一次房间预留");
            await Request(13, HttpMethod.Post, $"/api/user/13/diarkis-room/{reservedId}/no", NumberBody(), 200);
            clock.Advance(TimeSpan.FromSeconds(61));
            await Request(13, HttpMethod.Post, $"/api/user/13/diarkis-room/{reservedId}/no", NumberBody(), 404);
            check(store.GetUserIds().Order().SequenceEqual(new long[] { 11, 12, 13 }), "空预留到期回收，不新增或删除持久账号");
        }
        static byte[] NumberBody() => DumpSerializer.Serialize(new PrivateRoomNumberRequest { privateRoomType = "multi_live", liveRuleType = "normal" });
        static byte[] PatchBody(int lobby) => DumpSerializer.Serialize(new PrivateRoomNumberPatchRequest { multiLiveLobbyId = lobby, liveRuleType = "normal" });
    }

    private sealed class RewardRandom : Random
    {
        public int Choice { get; set; }
        public override int Next(int maxValue) => Choice % maxValue;
    }

    private sealed class Clock : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).Ticks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
        public void Set(DateTimeOffset value) => Interlocked.Exchange(ref ticks, value.Ticks);
    }

    [MessagePack.MessagePackObject(AllowPrivate = true)]
    public sealed class BrokenResponse
    {
        [MessagePack.Key(0)] public int Value => throw new InvalidOperationException("fixture-response");
    }
}
