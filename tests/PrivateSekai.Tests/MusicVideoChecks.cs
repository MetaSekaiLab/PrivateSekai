extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Live;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class MusicVideoChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "music-video-fixture");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "musicVocals.json"), """[{"id":20,"musicId":10},{"id":21,"musicId":11}]""");
        File.WriteAllText(Path.Combine(directory, "musicCategories.json"), """[{"id":1,"musicId":10,"musicCategoryName":"mv"},{"id":2,"musicId":11,"musicCategoryName":"image"}]""");
        File.WriteAllText(Path.Combine(directory, "beginnerMissionV2s.json"),
            """[{"id":31,"beginnerMissionV2Type":"watch_any_music_video_full","requirement":1},{"id":32,"beginnerMissionV2Type":"any_live_clear","requirement":1}]""");
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userMusics = [];
        state.Data.userMusicVocals = [];
        state.Data.userBeginnerMissionV2s = [];
        state.Data.userMissionStatuses = [];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton(_ => new CustomProfileThumbnailStore())
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<IUserStore>(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var videos = scope.ServiceProvider.GetRequiredService<MusicVideoService>();
        foreach (var (music, vocal, status, category) in new[]
        {
            (10, 20, "start", "mv"), (10, 21, "end", "mv"), (99, 20, "end", "mv"),
            (10, 99, "end", "mv"), (10, 20, "end", "mv_2d")
        })
        {
            var bytes = operations.Execute(1, () =>
            {
                videos.Record(music, Request(vocal, status, category));
                return user.BuildRefresh();
            });
            var refresh = DumpSerializer.Deserialize<SuiteUser>(bytes);
            Check.That(refresh.userBeginnerMissionV2s == null && refresh.userMissionStatuses == null &&
                store.Read(1)!.Data.userBeginnerMissionV2s.Length == 0, "开始或无匹配组合不增加 MV 任务、不刷新任务");
        }
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            videos.Record(10, Request(20, "end", "mv"));
            return new BrokenResponse();
        }), "MV 响应编码失败整体回滚");
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.Length == 0 &&
            store.Read(1)!.Data.userMissionStatuses.Length == 0, "MV 回滚不残留任务进度和达成状态");
        operations.Execute(1, () => { videos.Record(10, Request(20, "end", "mv")); return user.BuildRefresh(); });
        var saved = store.Read(1)!.Data;
        Check.That(saved.userBeginnerMissionV2s.Single().beginnerMissionV2Id == 31 &&
            saved.userBeginnerMissionV2s.Single().progress == 1 && saved.userMissionStatuses.Single().missionStatus == "achieved",
            "首次有效结束按 master 任务类型达成，任务编号不写死");
        Check.That(!saved.userBeginnerMissionV2s.Single().isNewAchieved && saved.userMusics.Length == 0 &&
            saved.userMusicVocals.Count == 0 && store.Read(2)!.Data.userBeginnerMissionV2s == null,
            "MV 不持久化首次达成提示、不解锁音乐音源、不影响其他用户");
        var repeated = operations.Execute(1, () => { videos.Record(10, Request(20, "end", "mv")); return user.BuildRefresh(); });
        var repeatedRefresh = DumpSerializer.Deserialize<SuiteUser>(repeated);
        Check.That(repeatedRefresh.userBeginnerMissionV2s.Single().progress == 2 && repeatedRefresh.userMissionStatuses == null,
            "重复结束继续计数但不再次标记任务达成");
        operations.Execute(1, () => { videos.Record(11, Request(21, "end", "image")); return user.BuildRefresh(); });
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.Single().progress == 3,
            "master 实际存在的 image 类型组合也计数");
        var received = store.Read(1)!;
        received.Data.userMissionStatuses.Single().missionStatus = "received";
        store.Save(1, received);
        operations.Execute(1, () => { videos.Record(10, Request(20, "end", "mv")); return user.BuildRefresh(); });
        Check.That(store.Read(1)!.Data.userBeginnerMissionV2s.Single().progress == 4 &&
            store.Read(1)!.Data.userMissionStatuses.Single().missionStatus == "received",
            "领奖后有效观看继续计数，保留已领取状态");
    }

    private static UserMusicVideoRequest Request(int vocal, string status, string category) => new()
    {
        musicVocal = vocal, musicPlayStatus = status, musicCategoryName = category
    };
}
