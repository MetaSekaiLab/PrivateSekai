extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using game::Sekai.CustomProfile;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Modules.Profiles;

public sealed class ProfileService(UserSession user, CustomProfileThumbnailStore thumbnails)
{
    public void SaveConfig(PostUserConfigRequest request)
    {
        if (request.defaultMusicType == null && request.isDisplayLoginStatus == null && request.friendRequestScope == null)
            return;
        var config = user.Data.userConfig ?? throw new InvalidOperationException("缺少用户配置。");
        if (request.defaultMusicType != null) config.defaultMusicType = request.defaultMusicType;
        if (request.isDisplayLoginStatus is { } display) config.isDisplayLoginStatus = display;
        if (request.friendRequestScope != null) config.friendRequestScope = request.friendRequestScope;
        user.MarkChanged(nameof(SuiteUser.userConfig));
    }

    public void SaveStampFavorites(UserStampFavoriteRequest request)
    {
        var resource = request.UserStampFavoriteResource ?? throw new ArgumentException("缺少表情收藏配置。");
        var tabs = resource.UserStampFavoriteTabs ?? throw new ArgumentException("缺少表情收藏页签。");
        var favorites = resource.userStampFavorites ?? throw new ArgumentException("缺少表情收藏列表。");
        if (tabs.Any(t => t == null || t.TabNum is < 0 or >= 3 || t.TabName == null ||
                (t.userId != 0 && t.userId != user.UserId)) ||
            tabs.Select(t => t.TabNum).Distinct().Count() != tabs.Length)
            throw new ArgumentException("表情收藏页签无效。");
        var owned = (user.Data.userStamps ?? []).Select(s => s.stampId).ToHashSet();
        if (favorites.Any(f => f == null || f.TabNum is < 0 or >= 3 || f.num is < 0 or >= 20 ||
                (f.userId != 0 && f.userId != user.UserId) || !owned.Contains(f.stampId)) ||
            favorites.Select(f => (f.TabNum, f.num)).Distinct().Count() != favorites.Length ||
            favorites.Select(f => (f.TabNum, f.stampId)).Distinct().Count() != favorites.Length)
            throw new ArgumentException("表情收藏记录无效。");
        var savedTabs = (user.Data.UserStampFavoriteTabs ?? []).ToDictionary(t => t.TabNum);
        var tabsChanged = false;
        foreach (var tab in tabs)
        {
            if (savedTabs.TryGetValue(tab.TabNum, out var current) && current.TabName == tab.TabName) continue;
            savedTabs[tab.TabNum] = new() { userId = user.UserId, TabNum = tab.TabNum, TabName = tab.TabName };
            tabsChanged = true;
        }
        if (tabsChanged)
        {
            user.Data.UserStampFavoriteTabs = savedTabs.Values.OrderBy(t => t.TabNum).ToArray();
            user.MarkChanged(nameof(SuiteUser.UserStampFavoriteTabs));
        }
        var savedFavorites = favorites.OrderBy(f => f.stampId).ThenBy(f => f.TabNum).ThenBy(f => f.num)
            .Select(f => new UserStampFavorite
            {
                userId = user.UserId, stampId = f.stampId, TabNum = f.TabNum, num = f.num
            }).ToArray();
        if (!(user.Data.userStampFavorites ?? []).OrderBy(f => f.stampId).ThenBy(f => f.TabNum).ThenBy(f => f.num)
            .Select(f => (f.stampId, f.TabNum, f.num)).SequenceEqual(savedFavorites.Select(f => (f.stampId, f.TabNum, f.num))))
        {
            user.Data.userStampFavorites = savedFavorites;
            user.MarkChanged(nameof(SuiteUser.userStampFavorites));
        }
    }

    public int UpdateProfile(PutUserProfileRequest request)
    {
        if (request.profileImageType == "leader")
        {
            if (request.profileImageId != null) return 400;
        }
        else
        {
            if (request.profileImageId == null) return 400;
            var card = (user.Data.userCards ?? []).SingleOrDefault(c => c.cardId == request.profileImageId);
            if (card == null) return 404;
            if (request.profileImageType == "card_after_special_training" && card.specialTrainingStatus != "done")
                return 409;
        }
        if (user.Data.userProfile == null) return 200;
        user.Data.userProfile = new UserProfile
        {
            userId = user.UserId, word = request.word, twitterId = request.twitterId,
            profileImageType = request.profileImageType, profileImageId = request.profileImageId ?? 0
        };
        user.MarkChanged(nameof(SuiteUser.userProfile));
        return 200;
    }

    public void SaveCustomProfile(
        int customProfileId,
        string? name,
        List<UserCustomProfileCardOrder>? customProfileCardOrders)
    {
        var profiles = user.Data.userCustomProfiles!.ToList();
        var profile = profiles.FirstOrDefault(p => p.customProfileId == customProfileId);
        if (profile == null)
        {
            profiles.Add(new UserCustomProfile
            {
                customProfileId = customProfileId,
                name = name ?? ""
            });
        }
        else if (name != null)
        {
            profile.name = name;
        }

        var cards = user.Data.userCustomProfileCards!.ToList();
        if (customProfileCardOrders != null)
        {
            foreach (var order in customProfileCardOrders.Where(o => o.customProfileId == customProfileId))
            {
                var card = cards.FirstOrDefault(c =>
                    c.customProfileId == customProfileId &&
                    c.customProfileCardId == order.customProfileCardId);
                if (card != null)
                    card.seq = order.seq;
            }
        }

        user.Data.userCustomProfiles = profiles.ToArray();
        user.Data.userCustomProfileCards = cards.ToArray();
        UpdateCustomProfileResourceUsages(customProfileId);

        user.MarkChanged(nameof(SuiteUser.userCustomProfiles));
        user.MarkChanged(nameof(SuiteUser.userCustomProfileCards));
    }

    public void SaveCustomProfileCard(
        int customProfileId,
        int customProfileCardId,
        UserSaveCustomProfileCardRequest request)
    {
        EnsureCustomProfileExists(customProfileId);

        var cards = user.Data.userCustomProfileCards!.ToList();
        var card = cards.FirstOrDefault(c =>
            c.customProfileId == customProfileId &&
            c.customProfileCardId == customProfileCardId);

        if (card == null)
        {
            var nextSeq = cards
                .Where(c => c.customProfileId == customProfileId)
                .Select(c => c.seq)
                .DefaultIfEmpty(0)
                .Max() + 1;

            var thumbnailPath = thumbnails.SaveThumbnail(request.thumbnail);
            cards.Add(new UserCustomProfileCard
            {
                customProfileId = customProfileId,
                customProfileCardId = customProfileCardId,
                thumbnailPath = thumbnailPath,
                customProfileCard = request.customProfileCard,
                seq = nextSeq
            });
        }
        else
        {
            card.thumbnailPath = thumbnails.SaveThumbnail(request.thumbnail, card.thumbnailPath);
            card.customProfileCard = request.customProfileCard;
        }

        user.Data.userCustomProfileCards = cards.ToArray();
        UpdateCustomProfileResourceUsages(customProfileId);
        user.MarkChanged(nameof(SuiteUser.userCustomProfileCards));
    }

    public void DeleteCustomProfileCards(int customProfileId, int[] customProfileCardIds)
    {
        var deleteIds = customProfileCardIds.ToHashSet();
        var cards = user.Data.userCustomProfileCards!
            .Where(c => c.customProfileId != customProfileId || !deleteIds.Contains(c.customProfileCardId))
            .ToList();

        var seq = 1;
        foreach (var card in cards
            .Where(c => c.customProfileId == customProfileId)
            .OrderBy(c => c.seq)
            .ThenBy(c => c.customProfileCardId))
        {
            card.seq = seq++;
        }

        user.Data.userCustomProfileCards = cards.ToArray();
        UpdateCustomProfileResourceUsages(customProfileId);
        user.MarkChanged(nameof(SuiteUser.userCustomProfileCards));
    }

    public void UpdateCustomProfileResourceUsages(int customProfileId)
    {
        var usageCounts = new Dictionary<int, int>();
        foreach (var card in user.Data.userCustomProfileCards!.Where(c => c.customProfileId == customProfileId))
        {
            var collections = card.customProfileCard?.collections;
            if (collections == null) continue;

            foreach (var collection in collections)
            {
                if (collection.id <= 0) continue;
                usageCounts.TryGetValue(collection.id, out var current);
                usageCounts[collection.id] = current + 1;
            }
        }

        var usages = user.Data.userCustomProfileResourceUsages!
            .Where(u => u.customProfileId != customProfileId)
            .ToList();

        usages.AddRange(usageCounts
            .OrderBy(kv => kv.Key)
            .Select(kv => new UserCustomProfileResourceUsages
            {
                customProfileId = customProfileId,
                customProfileResourceId = kv.Key,
                quantity = kv.Value
            }));

        user.Data.userCustomProfileResourceUsages = usages.ToArray();
        user.MarkChanged(nameof(SuiteUser.userCustomProfileResourceUsages));
    }

    private void EnsureCustomProfileExists(int customProfileId)
    {
        var profiles = user.Data.userCustomProfiles!.ToList();
        if (profiles.Any(p => p.customProfileId == customProfileId))
            return;

        profiles.Add(new UserCustomProfile
        {
            customProfileId = customProfileId,
            name = ""
        });
        user.Data.userCustomProfiles = profiles.ToArray();
        user.MarkChanged(nameof(SuiteUser.userCustomProfiles));
    }

    public void UpdateUserName(string newName)
    {
        if (user.Data.userGamedata == null)
            return;

        user.Data.userGamedata.name = newName;
        user.MarkChanged(nameof(SuiteUser.userGamedata));
    }
}
