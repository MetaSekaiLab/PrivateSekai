extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.CustomProfile;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Profiles;

public sealed class ProfileResourceHandler : IResourceHandler
{
    public IReadOnlyCollection<string> ResourceTypes { get; } =
        ["costume_3d", "avatar_motion", "avatar_costume", "avatar_accessory", "avatar_coordinate", "avatar_skin_color", "penlight", "stamp",
         "custom_profile_collection_resource"];

    public void Grant(UserSession user, UserResource resource)
    {
        if (resource.resourceId <= 0)
            return;

        switch (resource.resourceType)
        {
            case "custom_profile_collection_resource":
                var collections = (user.Data.userCustomProfileResources ?? []).ToList();
                var collection = collections.SingleOrDefault(c => c.customProfileResourceType == "collection" && c.customProfileResourceId == resource.resourceId);
                if (collection == null)
                {
                    collection = new UserCustomProfileResource { customProfileResourceType = "collection", customProfileResourceId = resource.resourceId };
                    collections.Add(collection);
                }
                collection.quantity = checked(collection.quantity + resource.quantity);
                user.Data.userCustomProfileResources = collections.ToArray();
                user.MarkChanged(nameof(SuiteUser.userCustomProfileResources));
                break;
            case "stamp":
                var stamps = (user.Data.userStamps ?? []).ToList();
                if (stamps.Any(s => s.stampId == resource.resourceId)) return;
                stamps.Add(new UserStamp { userId = user.UserId, stampId = resource.resourceId, obtainedAt = checked((ulong)user.Now) });
                user.Data.userStamps = stamps.OrderBy(s => s.stampId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userStamps));
                break;
            case "costume_3d":
                var costumes = (user.Data.userCostume3dStatuses ?? []).ToList();
                var costume = costumes.SingleOrDefault(c => c.costume3dId == resource.resourceId);
                if (costume?.status == "available") return;
                if (costume == null)
                {
                    costume = new UserCostume3DStatus { costume3dId = resource.resourceId };
                    costumes.Add(costume);
                }
                costume.obtainedAt = user.Now;
                costume.status = "available";
                user.Data.userCostume3dStatuses = costumes.OrderBy(c => c.costume3dId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userCostume3dStatuses));
                user.MarkChanged(nameof(SuiteUser.userCostume3dShopItems));
                break;
            case "avatar_motion":
                var motions = (user.Data.userAvatarMotions ?? []).ToList();
                if (motions.Any(m => m.avatarMotionId == resource.resourceId))
                    return;
                motions.Add(new UserAvatarMotion { avatarMotionId = resource.resourceId });
                user.Data.userAvatarMotions = motions.OrderBy(m => m.avatarMotionId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userAvatarMotions));
                break;
            case "avatar_costume":
                var avatarCostumes = (user.Data.userAvatarCostumes ?? []).ToList();
                if (avatarCostumes.Any(c => c.avatarCostumeId == resource.resourceId)) return;
                avatarCostumes.Add(new UserAvatarCostume { avatarCostumeId = resource.resourceId });
                user.Data.userAvatarCostumes = avatarCostumes.OrderBy(c => c.avatarCostumeId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userAvatarCostumes));
                break;
            case "avatar_accessory":
                var accessories = (user.Data.userAvatarAccessories ?? []).ToList();
                if (accessories.Any(a => a.avatarAccessoryId == resource.resourceId)) return;
                accessories.Add(new UserAvatarAccessory { avatarAccessoryId = resource.resourceId });
                user.Data.userAvatarAccessories = accessories.OrderBy(a => a.avatarAccessoryId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userAvatarAccessories));
                break;
            case "avatar_coordinate":
                var coordinates = (user.Data.userAvatarCoordinates ?? []).ToList();
                if (coordinates.Any(c => c.avatarCoordinateId == resource.resourceId)) return;
                coordinates.Add(new UserAvatarCoordinate { avatarCoordinateId = resource.resourceId });
                user.Data.userAvatarCoordinates = coordinates.OrderBy(c => c.avatarCoordinateId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userAvatarCoordinates));
                break;
            case "avatar_skin_color":
                var colors = (user.Data.userAvatarSkinColors ?? []).ToList();
                if (colors.Any(c => c.avatarSkinColorId == resource.resourceId)) return;
                colors.Add(new UserAvatarSkinColor { avatarSkinColorId = resource.resourceId });
                user.Data.userAvatarSkinColors = colors.OrderBy(c => c.avatarSkinColorId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userAvatarSkinColors));
                break;
            case "penlight":
                var penlights = (user.Data.userPenlights ?? []).ToList();
                if (penlights.Any(p => p.penlightId == resource.resourceId)) return;
                penlights.Add(new UserPenlight { penlightId = resource.resourceId });
                user.Data.userPenlights = penlights.OrderBy(p => p.penlightId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userPenlights));
                break;
            default:
                throw new NotSupportedException($"Resource '{resource.resourceType}' cannot be granted.");
        }
    }
}
