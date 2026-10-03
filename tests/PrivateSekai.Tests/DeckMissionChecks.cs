extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Live;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class DeckMissionChecks
{
    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../obj/deck-mission-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "liveMissions.json"), """
                [{"id":1,"liveMissionPeriodId":1,"liveMissionType":"free","requirement":25,"rewards":[{"resourceBoxId":1}]},
                 {"id":2,"liveMissionPeriodId":1,"liveMissionType":"premium","requirement":25,"rewards":[{"resourceBoxId":1}]},
                 {"id":3,"liveMissionPeriodId":1,"liveMissionType":"free","requirement":50,"rewards":[{"resourceBoxId":1}]}]
                """);
            File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"),
                """[{"id":1,"resourceBoxPurpose":"mission_reward","details":[{"resourceType":"material","resourceId":10,"resourceQuantity":5}]}]""");
            File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"),
                """[{"id":1,"beginnerMissionV2Type":"any_live_clear","requirement":3}]""");
            var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
            var store = new MemoryUserStore();
            using var provider = new ServiceCollection().AddPrivateSekai()
                .AddSingleton(_ => new PrivateSekai.Storage.CustomProfileThumbnailStore())
                .AddSingleton(master).AddSingleton<IUserStore>(store).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            var decks = scope.ServiceProvider.GetRequiredService<DeckService>();
            var missions = scope.ServiceProvider.GetRequiredService<MissionService>();
            var state = TestUsers.Create(1);
            state.Data.userGamedata.deck = 1;
            state.Data.userCards = [new() { cardId = 1 }, new() { cardId = 2 }];
            state.Data.userDecks = [new() { userId = 1, deckId = 1, name = "旧队伍", member1 = 1, leader = 1 }];
            state.Data.userLiveMissions = [new() { userId = 1, liveMissionPeriodId = 1, liveMissionStatus = "free", progress = 25 }];
            state.Data.userMissionStatuses = [new() { userId = 1, missionType = "live_mission", missionId = 1, missionStatus = "achieved" },
                new() { userId = 1, missionType = "live_mission", missionId = 2, missionStatus = "achieved" }];
            store.Save(1, state);
            var request = new PutUserDeckRequest
            {
                userDeckUpdates = [new() { userDeck = new() { deckId = 2, name = "新队伍", member1 = 1, member2 = 2, leader = 2, subLeader = 1 } }],
                mainDeckId = 2
            };
            Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
            {
                decks.Save(request);
                return new BrokenResponse();
            }), "编队保存编码失败回滚主编队和成员");
            Check.That(store.Read(1)!.Data.userGamedata.deck == 1 && store.Read(1)!.Data.userDecks.Length == 1,
                "失败保存不残留新编队");
            var response = DumpSerializer.Deserialize<UserDeckResponse>(operations.Execute(1, () =>
            {
                decks.Save(request);
                return new UserDeckResponse { updatedResources = user.BuildRefresh() };
            }));
            Check.That(response.updatedResources.userGamedata.deck == 2 &&
                response.updatedResources.userDecks.Single(d => d.deckId == 2).leader == 2,
                "保存成员和队长并切换主编队，响应包含两类状态");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
            {
                decks.Save(new() { userDeckUpdates = [new() { isDeleted = true, userDeck = new() { deckId = 2 } }] });
                return user.BuildRefresh();
            }), "不可删除主编队而不指定有效替代");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
            {
                decks.Save(new() { userDeckUpdates = [new() { userDeck = new() { deckId = 3, member1 = 99, leader = 99 } }] });
                return user.BuildRefresh();
            }), "不可把未持有卡牌写入编队");
            operations.Execute(1, () =>
            {
                decks.Save(new() { userDeckUpdates = [new() { isDeleted = true, userDeck = new() { deckId = 2 } }], mainDeckId = 1 });
                return user.BuildRefresh();
            });
            Check.That(store.Read(1)!.Data.userGamedata.deck == 1 && store.Read(1)!.Data.userDecks.Single().deckId == 1,
                "删除与主编队切换可在同一操作完成");

            Check.Throws<ArgumentException>(() => operations.Execute(1, () => missions.ReceiveLiveMissionRewards([3])),
                "Live 任务缺少 achieved 状态不能领奖");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () => missions.ReceiveLiveMissionRewards([2])),
                "没有付费通行证不可领取 premium 奖励");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () => missions.ReceiveLiveMissionRewards([1, 999])),
                "批量领奖中包含未知任务时整体回滚");
            Check.That(store.Read(1)!.Data.userMissionStatuses.First().missionStatus == "achieved" &&
                !store.Read(1)!.Data.userMaterials.Any(m => m.materialId == 10), "批量失败不标记已领取、不发放部分奖励");
            Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
            {
                missions.ReceiveLiveMissionRewards([1]);
                return new BrokenResponse();
            }), "Live 任务响应编码失败回滚发奖");
            var received = DumpSerializer.Deserialize<UserMissionReceiveResponse>(operations.Execute(1, () => new UserMissionReceiveResponse
            {
                ObtainedRewards = missions.ReceiveLiveMissionRewards([1, 1]), UpdatedResources = user.BuildRefresh()
            }));
            var repeated = DumpSerializer.Deserialize<UserResource[]>(operations.Execute(1, () => missions.ReceiveLiveMissionRewards([1])));
            Check.That(received.ObtainedRewards.Single().quantity == 5 && repeated.Length == 0 &&
                store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 10).quantity == 5,
                "Live 任务重复 ID 与重复请求均不重复发奖");
            Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
            {
                missions.UpdateLiveMissionProgress(new() { liveMissionPeriodId = 1, addNormalProgress = 25 });
                return new BrokenResponse();
            }), "Live 任务跨门槛后编码失败回滚进度和达成状态");
            Check.That(store.Read(1)!.Data.userLiveMissions.Single().progress == 25 &&
                !store.Read(1)!.Data.userMissionStatuses.Any(s => s.missionId == 3), "失败结算不残留达成状态");
            var achieved = DumpSerializer.Deserialize<SuiteUser>(operations.Execute(1, () =>
            {
                missions.UpdateLiveMissionProgress(new() { liveMissionPeriodId = 1, addNormalProgress = 25 });
                return user.BuildRefresh();
            }));
            Check.That(achieved.userMissionStatuses.Single(s => s.missionId == 3).missionStatus == "achieved" &&
                achieved.userMissionStatuses.Single(s => s.missionId == 1).missionStatus == "received" &&
                achieved.userLiveMissions.Single().achievedMissionIds.Length == 0,
                "免费任务达到门槛后新增状态，保留已领奖状态和独立历史数组");
            var unchanged = DumpSerializer.Deserialize<SuiteUser>(operations.Execute(1, () =>
            {
                missions.UpdateLiveMissionProgress(new() { liveMissionPeriodId = 1, addNormalProgress = 1 });
                return user.BuildRefresh();
            }));
            Check.That(unchanged.userMissionStatuses == null, "没有新达成任务时不重复刷新任务状态");
            Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
            {
                missions.RecordManualLiveClear();
                return new BrokenResponse();
            }), "新手演出任务创建进度后编码失败整体回滚");
            Check.That(!(store.Read(1)!.Data.userBeginnerMissionV2s ?? []).Any(m => m.beginnerMissionV2Id == 1),
                "失败结算不保留新手演出进度");
            Console.WriteLine("编队与任务：保存、主编队切换、任务达成、领奖条件及回滚检查通过。");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
