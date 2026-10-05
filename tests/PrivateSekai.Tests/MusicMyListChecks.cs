extern alias game;

using System;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Modules.Music;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class MusicMyListChecks
{
    public static void Run()
    {
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userMusicMyList = [new() { listNo = 5, name = "Other", musicIds = [2] }];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var lists = new MusicMyListService(user);
        var request = new PutUserMusicMyListRequest { name = "Test", musicIds = [6, 1, 2] };
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            lists.Save(1, request);
            return new BrokenResponse();
        }), "My List 编码失败回滚保存");
        Check.That(store.Read(1)!.Data.userMusicMyList.Single().listNo == 5, "保存失败保留原列表");
        var bytes = operation.Execute(1, () =>
        {
            Check.That(lists.Save(1, request) == 200, "新建 My List 成功");
            return new PutUserMusicMyListResponse { updatedResources = user.BuildRefresh() };
        });
        var refresh = DumpSerializer.Deserialize<PutUserMusicMyListResponse>(bytes).updatedResources;
        Check.That(refresh.userMusicMyList.Select(l => l.listNo).SequenceEqual(new[] { 1, 5 }) &&
            refresh.userMusicMyList[0].musicIds.SequenceEqual(new[] { 1, 2, 6 }) &&
            refresh.userMusicMyList[1].name == "Other", "保存排序歌曲并返回全部列表，保留其他列表");
        Check.That(request.musicIds.SequenceEqual(new[] { 6, 1, 2 }) && store.Read(2)!.Data.userMusicMyList == null,
            "保存不修改输入数组或其他账号");
        operation.Execute(1, () =>
        {
            Check.That(lists.Save(1, request) == 400 && user.BuildRefresh().userMusicMyList == null,
                "重复保存返回 400 且不标记列表刷新");
            return null;
        });
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            lists.Reset(1);
            return new BrokenResponse();
        }), "My List 编码失败回滚重置");
        Check.That(store.Read(1)!.Data.userMusicMyList[0].musicIds.Length == 3, "重置编码失败保留歌曲");
        operation.Execute(1, () =>
        {
            Check.That(lists.Reset(1) == 200, "重置已有歌曲成功");
            return user.BuildRefresh();
        });
        var saved = store.Read(1)!.Data.userMusicMyList;
        Check.That(saved[0].name == "Test" && saved[0].musicIds.Length == 0 && saved[1].musicIds.Single() == 2,
            "重置清空歌曲并保留名称、列表和其他列表歌曲");
        operation.Execute(1, () =>
        {
            Check.That(lists.Reset(1) == 404 && lists.Reset(3) == 404, "空列表及不存在列表重置返回 404");
            return null;
        });
        request.name = "Renamed";
        request.musicIds = [];
        operation.Execute(1, () =>
        {
            Check.That(lists.Save(1, request) == 200, "空列表可以改名");
            return null;
        });
        foreach (var listNo in new[] { 0, 6 })
            Check.Throws<ArgumentException>(() => operation.Execute(1, () => lists.Save(listNo, request)),
                "本地拒绝客户端范围外的列表号");
    }
}
