extern alias game;

using System;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Profiles;

public sealed class ProfileHonorService(UserSession user, MasterData master)
{
    public int Save(PutUserProfileHonorRequest request)
    {
        var honors = request.profileHonors ?? throw new ArgumentException("缺少称号列表。");
        if (honors.Any(h => h == null || h.seq is < 1 or > 3 || h.profileHonorType != "normal" ||
                h.bondsHonorViewType != "none" || h.bondsHonorWordId != 0) ||
            honors.Select(h => h.seq).Distinct().Count() != honors.Count)
            throw new ArgumentException("称号配置超出已支持范围。");
        var owned = (user.Data.userHonors ?? []).ToDictionary(h => h.honorId);
        foreach (var honor in honors)
        {
            if (!owned.ContainsKey(honor.honorId)) return 409;
            var definition = master.GetTable<MasterHonor>("honors", h => h.id).FindById(honor.honorId)
                ?? throw new InvalidOperationException("缺少持有称号的 master 定义。");
            if (honor.honorBackgroundId is { } background &&
                (!(user.Data.userHonorBackgrounds ?? []).Any(h => h.honorBackgroundId == background) ||
                 master.GetTable<MasterHonorBackground>("honorBackgrounds", h => h.id).FindById(background)?.honorGroupId != definition.groupId))
                return 409;
            if (honor.honorWordId is { } word &&
                (!(user.Data.userHonorWords ?? []).Any(h => h.honorWordId == word) ||
                 master.GetTable<MasterHonorWord>("honorWords", h => h.id).FindById(word)?.honorGroupId != definition.groupId))
                return 409;
        }
        user.Data.userProfileHonors = honors.OrderBy(h => h.seq).Select(h => new UserProfileHonor
        {
            seq = h.seq, profileHonorType = h.profileHonorType, honorId = h.honorId, honorLevel = owned[h.honorId].level,
            bondsHonorViewType = h.bondsHonorViewType, bondsHonorWordId = h.bondsHonorWordId,
            honorBackgroundId = h.honorBackgroundId, honorWordId = h.honorWordId
        }).ToArray();
        user.MarkChanged(nameof(SuiteUser.userProfileHonors));
        return 200;
    }
}
