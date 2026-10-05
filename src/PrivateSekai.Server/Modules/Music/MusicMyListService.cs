extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Music;

public sealed class MusicMyListService(UserSession user)
{
    public int Save(int listNo, PutUserMusicMyListRequest request)
    {
        if (listNo is < 1 or > 5 || string.IsNullOrEmpty(request.name) || request.musicIds == null ||
            request.musicIds.Any(id => id <= 0))
            throw new ArgumentException("乐曲列表参数无效。");

        var lists = user.Data.userMusicMyList ?? [];
        var current = lists.SingleOrDefault(l => l.listNo == listNo);
        var ids = request.musicIds.Order().ToArray();
        if (current != null && current.name == request.name && (current.musicIds ?? []).SequenceEqual(ids))
            return 400;

        var saved = new UserMusicMyList { listNo = listNo, name = request.name, musicIds = ids };
        user.Data.userMusicMyList = lists.Where(l => l.listNo != listNo).Append(saved).OrderBy(l => l.listNo).ToArray();
        user.MarkChanged(nameof(SuiteUser.userMusicMyList));
        return 200;
    }

    public int Reset(int listNo)
    {
        if (listNo is < 1 or > 5)
            throw new ArgumentException("乐曲列表编号无效。");
        var current = (user.Data.userMusicMyList ?? []).SingleOrDefault(l => l.listNo == listNo);
        if (current?.musicIds == null || current.musicIds.Length == 0)
            return 404;
        current.musicIds = [];
        user.MarkChanged(nameof(SuiteUser.userMusicMyList));
        return 200;
    }
}
