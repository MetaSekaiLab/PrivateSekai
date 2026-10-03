extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Live;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class ChallengeStageChecks
{
    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/challenge-stage-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "challengeLiveStages.json"), """
            [{"id":101,"characterId":1,"rank":1,"nextStageChallengePoint":50,"completeStageResourceBoxId":7,"completeStageCharacterExp":1},
             {"id":102,"characterId":1,"rank":2,"nextStageChallengePoint":100,"completeStageResourceBoxId":7,"completeStageCharacterExp":2},
             {"id":103,"characterId":1,"rank":3,"nextStageChallengePoint":200,"completeStageResourceBoxId":7,"completeStageCharacterExp":3}]
            """);
        File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
            [{"id":7,"resourceBoxPurpose":"challenge_live_high_score","details":[{"resourceType":"jewel","resourceQuantity":999}]},
             {"id":7,"resourceBoxPurpose":"challenge_live_stage","details":[{"resourceType":"jewel","resourceQuantity":50}]}]
            """);
        var store = new MemoryUserStore();
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton<IUserStore>(store)
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory)).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var challenges = scope.ServiceProvider.GetRequiredService<ChallengeLiveService>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        void Reset() => store.Save(1, TestUsers.Create(1));
        void Advance(int point) => operations.Execute(1, () =>
        {
            challenges.AdvanceStage(1, point);
            return user.BuildRefresh();
        });
        Reset();
        Advance(49);
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloStages.Single().point == 49 &&
            store.Read(1)!.Data.userChargedCurrency == null, "挑战未达门槛不发阶段奖励");
        operations.Execute(1, () =>
        {
            var result = challenges.AdvanceStage(1, 1);
            Check.That(result.BeforeRank == 1 && result.BeforePoint == 49 && result.AfterRank == 2 &&
                result.AfterPoint == 0 && result.CharacterExp == 1, "挑战恰好达到门槛进入下一级零余点");
            return user.BuildRefresh();
        });
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloStages.First().point == 50 &&
            store.Read(1)!.Data.userChallengeLiveSoloStages.First().challengeLiveStageStatus == "complete" &&
            store.Read(1)!.Data.userChargedCurrency.free == 50, "保留已完成阶段并按用途选择奖励盒");
        Advance(10);
        Check.That(store.Read(1)!.Data.userChargedCurrency.free == 50 &&
            store.Read(1)!.Data.userChallengeLiveSoloStages.Last().point == 10, "后续点数不重复发放已完成阶段奖励");
        Reset();
        operations.Execute(1, () =>
        {
            var result = challenges.AdvanceStage(1, 201);
            Check.That(result.AfterRank == 3 && result.AfterPoint == 51 && result.CharacterExp == 3 &&
                result.Rewards.Length == 2, "跨阶段累加逐级角色经验，保留两份阶段奖励");
            var refresh = user.BuildRefresh();
            Check.That(refresh.userChallengeLiveSoloStages.Length == 3 && refresh.userChargedCurrency.free == 100,
                "挑战阶段与奖励一并刷新");
            return refresh;
        });
        Reset();
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            challenges.AdvanceStage(1, 201);
            return new BrokenResponse();
        }), "挑战阶段结果编码失败");
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloStages == null && store.Read(1)!.Data.userChargedCurrency == null,
            "编码失败回滚全部阶段与奖励");
        Check.Throws<NotSupportedException>(() => Advance(350), "尚未核验的 EX 阶段拒绝结算");
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloStages == null && store.Read(1)!.Data.userChargedCurrency == null,
            "跨入不支持阶段不提交此前奖励");
        Check.Throws<ArgumentOutOfRangeException>(() => Advance(-1), "拒绝负挑战点数");
        var other = TestUsers.Create(1);
        other.Data.userChallengeLiveSoloStages = [new() { characterId = 2, rank = 1, point = 9,
            challengeLiveStageId = 201, challengeLiveStageType = "normal", challengeLiveStageStatus = "in_progress" }];
        store.Save(1, other);
        Advance(201);
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloStages.First().characterId == 2 &&
            store.Read(1)!.Data.userChallengeLiveSoloStages.First().point == 9, "挑战结算保留其他角色阶段");
    }
}
