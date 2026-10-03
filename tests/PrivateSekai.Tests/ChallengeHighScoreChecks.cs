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

internal static class ChallengeHighScoreChecks
{
    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/challenge-score-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "challengeLiveHighScoreRewards.json"), """
            [{"id":1,"characterId":1,"highScore":100,"resourceBoxId":7},
             {"id":2,"characterId":1,"highScore":200,"resourceBoxId":7},
             {"id":3,"characterId":2,"highScore":100,"resourceBoxId":8}]
            """);
        File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
            [{"id":7,"resourceBoxPurpose":"challenge_live_stage","details":[{"resourceType":"jewel","resourceQuantity":999}]},
             {"id":7,"resourceBoxPurpose":"challenge_live_high_score","details":[{"resourceType":"jewel","resourceQuantity":100}]}]
            """);
        var store = new MemoryUserStore();
        using var provider = new ServiceCollection().AddPrivateSekai().AddSingleton<IUserStore>(store)
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory)).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var service = scope.ServiceProvider.GetRequiredService<ChallengeLiveService>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        void Reset() => store.Save(1, TestUsers.Create(1));
        UserChallengeLiveHighScoreResult Update(int score)
        {
            UserChallengeLiveHighScoreResult result = null!;
            operations.Execute(1, () => { result = service.UpdateHighScore(1, score); return user.BuildRefresh(); });
            return result;
        }
        Reset();
        var result = Update(99);
        Check.That(result.beforeHighScore == 0 && result.afterHighScore == 99 && result.rewards.Length == 0 &&
            store.Read(1)!.Data.userChallengeLiveSoloHighScoreRewards.Length == 0, "未达高分门槛只保存成绩");
        result = Update(100);
        Check.That(result.beforeHighScore == 99 && result.afterHighScore == 100 &&
            result.rewards.Single().challengeLiveHighScoreId == 1 && result.rewards.Single().userResources.Single().quantity == 100,
            "恰达门槛返回 master 条目 ID 与该用途的资源盒");
        var reward = store.Read(1)!.Data.userChallengeLiveSoloHighScoreRewards.Single();
        Check.That(reward.characterId == 1 && reward.challengeLiveHighScoreRewardId == 1 && reward.challengeLiveHighScoreStatus == "complete",
            "高分用户记录与响应使用各自协议字段");
        Check.That(Update(100).rewards.Length == 0 && Update(50).afterHighScore == 100 &&
            store.Read(1)!.Data.userChargedCurrency.free == 100, "重复或较低成绩不降低最高分或重复发奖");
        Reset();
        result = Update(200);
        Check.That(result.rewards.Select(r => r.challengeLiveHighScoreId).SequenceEqual([1, 2]) &&
            store.Read(1)!.Data.userChargedCurrency.free == 200, "多门槛按分数顺序逐条发奖且不串角色");
        Reset();
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            service.UpdateHighScore(1, 200);
            return new BrokenResponse();
        }), "高分结算编码失败");
        Check.That(store.Read(1)!.Data.userChallengeLiveSoloResults == null &&
            store.Read(1)!.Data.userChallengeLiveSoloHighScoreRewards == null && store.Read(1)!.Data.userChargedCurrency == null,
            "高分结算编码失败同时回滚成绩、领奖记录与资源");
        Check.Throws<InvalidOperationException>(() => operations.Execute(1, () => service.UpdateHighScore(2, 100)),
            "高分奖励缺少资源盒时不保存空奖励记录");
    }
}
