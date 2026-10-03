extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.ApiData;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Profiles;

public sealed class HonorResourceHandler : IResourceHandler
{
    public IReadOnlyCollection<string> ResourceTypes { get; } = ["honor", "honor_background", "honor_word"];

    public void Grant(UserSession user, UserResource resource)
    {
        if (resource.resourceId <= 0 || resource.quantity != 1)
            throw new ArgumentException("Invalid honor resource.");
        switch (resource.resourceType)
        {
            case "honor":
                if ((user.Data.userHonors ?? []).Any(h => h.honorId == resource.resourceId) || resource.resourceLevel != 1)
                    throw new NotSupportedException("Existing honor upgrades are not verified.");
                user.Data.userHonors = (user.Data.userHonors ?? []).Append(new UserHonor
                {
                    userId = user.UserId, honorId = resource.resourceId, level = resource.resourceLevel, obtainedAt = user.Now
                }).OrderBy(h => h.honorId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userHonors));
                user.MarkChanged(nameof(SuiteUser.userProfile));
                user.MarkChanged(nameof(SuiteUser.userProfileHonors));
                break;
            case "honor_background":
                if ((user.Data.userHonorBackgrounds ?? []).Any(h => h.honorBackgroundId == resource.resourceId))
                    throw new NotSupportedException("Duplicate honor backgrounds are not verified.");
                user.Data.userHonorBackgrounds = (user.Data.userHonorBackgrounds ?? []).Append(new UserHonorBackground
                {
                    honorBackgroundId = resource.resourceId, obtainedAt = user.Now
                }).OrderBy(h => h.honorBackgroundId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userHonorBackgrounds));
                break;
            case "honor_word":
                if ((user.Data.userHonorWords ?? []).Any(h => h.honorWordId == resource.resourceId))
                    throw new NotSupportedException("Duplicate honor words are not verified.");
                user.Data.userHonorWords = (user.Data.userHonorWords ?? []).Append(new UserHonorWord
                {
                    honorWordId = resource.resourceId, obtainedAt = user.Now
                }).OrderBy(h => h.honorWordId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userHonorWords));
                break;
            default: throw new NotSupportedException("Unsupported honor resource.");
        }
    }
}
