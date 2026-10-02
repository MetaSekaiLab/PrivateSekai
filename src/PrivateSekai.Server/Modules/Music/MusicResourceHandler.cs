extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Music;

public sealed class MusicResourceHandler(ResourceMasterQueries master) : IResourceHandler
{
    public IReadOnlyCollection<string> ResourceTypes { get; } = ["music", "music_vocal"];

    public void Grant(UserSession user, UserResource resource)
    {
        if (resource.resourceId <= 0)
            return;

        switch (resource.resourceType)
        {
            case "music":
                var musics = (user.Data.userMusics ?? []).ToList();
                if (musics.All(m => m.musicId != resource.resourceId))
                {
                    musics.Add(new UserMusic { musicId = resource.resourceId });
                    user.Data.userMusics = musics.OrderBy(m => m.musicId).ToArray();
                    user.MarkChanged(nameof(SuiteUser.userMusics));
                }
                foreach (var vocal in master.GetMasterMusicVocals())
                {
                    if (vocal.musicId == resource.resourceId && vocal.releaseConditionId == 5)
                        GrantVocal(user, vocal.musicId, vocal.id);
                }
                break;
            case "music_vocal":
                var selected = master.GetMasterMusicVocal(resource.resourceId);
                if (selected != null)
                    GrantVocal(user, selected.musicId, resource.resourceId);
                break;
            default:
                throw new NotSupportedException($"Resource '{resource.resourceType}' cannot be granted.");
        }
    }

    private static void GrantVocal(UserSession user, int musicId, int vocalId)
    {
        if (musicId <= 0 || vocalId <= 0)
            return;

        user.Data.userMusicVocals ??= [];
        if (user.Data.userMusicVocals.Any(v => v.musicVocalId == vocalId))
            return;

        user.Data.userMusicVocals.Add(new UserMusicVocal { musicId = musicId, musicVocalId = vocalId });
        user.Data.userMusicVocals = user.Data.userMusicVocals.OrderBy(v => v.musicVocalId).ToList();
        user.MarkChanged(nameof(SuiteUser.userMusicVocals));
    }
}
