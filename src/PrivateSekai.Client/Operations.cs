extern alias game;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using game::Sekai;
using game::Sekai.ApiData;
using game::Sekai.CustomProfile;
using PrivateSekai.Protocol;

namespace PrivateSekai.Client;

public sealed record OperationDefinition(string Method, string Path, Type? RequestType, string? RequiredResponseField,
    bool Snapshot = true, string[]? BooleanQueries = null, bool OptionalBody = false,
    string[]? RequiredIntegerListQueries = null, Dictionary<string, int>? RequiredIntegerQueries = null)
{
    public bool IsWrite => Method != "GET";
}

public static class Operations
{
    public static readonly IReadOnlyDictionary<string, OperationDefinition> All = new Dictionary<string, OperationDefinition>
    {
        ["system"] = new("GET", "/api/system", null, "appVersions", false),
        ["thumbnail-download"] = new("GET", "<previous-response-thumbnail>", null, "sha256", false),
        ["suite"] = new("GET", "/api/suite/user/{userId}", null, "userRegistration", false),
        ["suite-friends"] = new("GET", "/api/suite/user/{userId}/parts?name=user_friend", null, "now", false),
        ["suite-break-time"] = new("GET", "/api/suite/user/{userId}/parts?name=user_event_break_time", null, "now", false),
        ["account-restrict-info"] = new("GET", "/api/user/{userId}/restrict-info", null, "isRestrictDeviceTransfer", false),
        ["inherit-set"] = new("PUT", "/api/user/{userId}/inherit", typeof(UserIPassInheritRequest), "userInherit"),
        ["inherit-preview"] = new("POST", "/api/inherit/user/{inheritId}?isExecuteInherit=False", null, "afterUserGamedata", false),
        ["inherit-execute"] = new("POST", "/api/inherit/user/{inheritId}?isExecuteInherit=True", null, "credential", false),
        ["information"] = new("GET", "/api/information", null, "informations", false),
        ["home-refresh"] = new("PUT", "/api/user/{userId}/home/refresh", typeof(UserHomeRefreshRequest), "updatedResources", OptionalBody: true),
        ["topic-read"] = new("PUT", "/api/user/{userId}/topic/{topicId}", null, "updatedResources"),
        ["appeal-read"] = new("PUT", "/api/user/{userId}/appeal", typeof(UserAppealRequest), "updatedResources"),
        ["present-history"] = new("GET", "/api/user/{userId}/present/history", null, "userPresentHistories", false),
        ["boost-item"] = new("POST", "/api/user/{userId}/boost-item", typeof(UserBoostItemRequest), "updatedResources"),
        ["present-receive"] = new("POST", "/api/user/{userId}/present", typeof(UserPresentAPIRequest), "receivedUserPresents"),
        ["shop-purchase"] = new("POST", "/api/user/{userId}/shop/{shopId}/item/{shopItemId}", null, "updatedResources"),
        ["character-costume-save"] = new("PUT", "/api/user/{userId}/character-costume-3d/character/{characterId}/unit/{unit}", typeof(UserCharacterCostume3DRequest), "updatedResources"),
        ["costume-craft"] = new("POST", "/api/user/{userId}/costume-3d-shop/{shopItemId}", null, "updatedResources"),
        ["shop-upgrade"] = new("PUT", "/api/user/{userId}/shop/{shopId}/item/{shopItemId}", null, "updatedResources"),
        ["material-exchange"] = new("PUT", "/api/user/{userId}/material-exchange/{materialExchangeId}", null, "updatedResources",
            RequiredIntegerQueries: new() { ["costGroupId"] = 0, ["count"] = 1 }),
        ["event-exchange"] = new("PUT", "/api/user/{userId}/event-exchange/{eventExchangeId}", null, "obtainUserResources",
            RequiredIntegerQueries: new() { ["count"] = 1 }),
        ["gacha-draw"] = new("PUT", "/api/user/{userId}/gacha/{gachaId}/gachaBehaviorId/{gachaBehaviorId}", null, "obtainPrizes", BooleanQueries: ["isPriorityUsePaidJewel"]),
        ["gacha-exchange"] = new("PUT", "/api/user/{userId}/exchange/gacha-ceil-item", typeof(UserGachaCeilExchangeRequest), "obtainUserResources"),
        ["gacha-wish"] = new("PUT", "/api/user/{userId}/rate-choice-gacha-wish", typeof(UserRateChoiceGachaWishRequest), "updatedResources"),
        ["register"] = new("POST", "/api/user", typeof(UserAPIRequest), "userRegistration", false),
        ["auth"] = new("PUT", "/api/user/{userId}/auth?refreshUpdatedResources=False", typeof(UserAuthRequest), "sessionToken", false),
        ["tutorial"] = new("PATCH", "/api/user/{userId}/tutorial", typeof(UserTutorialRequest), "updatedResources"),
        ["user-name"] = new("PATCH", "/api/user/{userId}", typeof(UserNameAPIRequest), "updatedResources"),
        ["profile-save"] = new("PUT", "/api/user/{userId}/profile", typeof(PutUserProfileRequest), "updatedResources"),
        ["custom-profile-save"] = new("PUT", "/api/user/{userId}/custom-profile/{customProfileId}", typeof(UserSaveCustomProfileRequest), "updatedResources"),
        ["custom-profile-card-create"] = new("POST", "/api/user/{userId}/custom-profile/{customProfileId}/custom-profile-card/{customProfileCardId}", typeof(UserSaveCustomProfileCardRequest), "updatedResources"),
        ["custom-profile-card-update"] = new("PUT", "/api/user/{userId}/custom-profile/{customProfileId}/custom-profile-card/{customProfileCardId}", typeof(UserSaveCustomProfileCardRequest), "updatedResources"),
        ["custom-profile-card-delete"] = new("DELETE", "/api/user/{userId}/custom-profile/{customProfileId}/custom-profile-card", null, "updatedResources", RequiredIntegerListQueries: ["customProfileCardId"]),
        ["skill-ticket"] = new("POST", "/api/user/{userId}/card/{cardId}/skill-practice-ticket", typeof(UserCardSkillPracticeTicketRequest), "updateExpResult"),
        ["skill-material"] = new("POST", "/api/user/{userId}/card/{cardId}/material", typeof(UserCardSkillPracticeMaterialRequest), "updateExpResult"),
        ["special-training"] = new("PUT", "/api/user/{userId}/card/{cardId}?behavior=special_training", typeof(UserCardSpecialTrainingRequest), "updatedResources"),
        ["card-practice"] = new("POST", "/api/user/{userId}/card/{cardId}/practice-ticket", typeof(UserCardPracticeTicketRequest), "updateExpResult"),
        ["card-master-lesson"] = new("POST", "/api/user/{userId}/card/{cardId}/master-lesson", typeof(PostUserCardMasterLessonAPIRequest), "obtainedRewards"),
        ["card-default-image"] = new("PUT", "/api/user/{userId}/card/{cardId}?behavior=set_default_image", typeof(UserCardDefaultImageRequest), "updatedResources"),
        ["card-exchange"] = new("PUT", "/api/user/{userId}/card?behavior=exchange", typeof(UserCardExchangeRequest), "updatedResources"),
        ["deck-save"] = new("PUT", "/api/user/{userId}/deck", typeof(PutUserDeckRequest), "updatedResources"),
        ["live-start"] = new("POST", "/api/user/{userId}/live", typeof(UserLiveRequest), "userLiveId"),
        ["live-clear"] = new("PUT", "/api/user/{userId}/live/{userLiveId}", typeof(UserLiveClearRequest), "score"),
        ["challenge-character-unlock"] = new("POST", "/api/user/{userId}/challenge-live-character/{characterId}", null, "updatedResources"),
        ["challenge-deck-save"] = new("PUT", "/api/user/{userId}/challenge-live-solo-deck/{characterId}", typeof(UserChallengeLiveSoloDeck), "updatedResources"),
        ["challenge-live-start"] = new("POST", "/api/user/{userId}/challenge-live/solo", typeof(UserChallengeLiveStartRequest), "userChallengeLiveId"),
        ["challenge-live-clear"] = new("PUT", "/api/user/{userId}/challenge-live/solo/{userChallengeLiveId}", typeof(UserChallengeLiveClearRequest), "userChallengeLiveStageResult"),
        ["challenge-reward-receive"] = new("POST", "/api/user/{userId}/challenge-live/receive-select-reward/{resourceId}", null, "obtainRewards"),
        ["live-voice"] = new("POST", "/api/user/{userId}/live-character-archive-voice/live-result", typeof(UserLiveCharacterArchiveVoiceLiveResultRequest), "updatedResources"),
        ["bookmark-list"] = new("GET", "/api/user/{userId}/story-episode-bookmark/{storyType}/story/{storyId}", null, "userStoryEpisodeBookmarks", false),
        ["bookmark-add"] = new("POST", "/api/user/{userId}/story-episode-bookmark/{storyType}/story/{storyId}/episode/{episodeId}/talk/{talkId}", typeof(PostStoryEpisodeBookmarkRequest), "userStoryEpisodeBookmark"),
        ["bookmark-rename"] = new("PATCH", "/api/user/{userId}/story-episode-bookmark/{storyType}/story/{storyId}/episode/{episodeId}/talk/{talkId}", typeof(PatchStoryEpisodeBookmarkRequest), "storyId"),
        ["bookmark-delete"] = new("DELETE", "/api/user/{userId}/story-episode-bookmark/{storyType}/story/{storyId}/episode/{episodeId}/talk/{talkId}", null, "userStoryEpisodeBookmarks"),
        ["bookmark-click"] = new("POST", "/api/user/{userId}/story-episode-bookmark/{storyType}/story/{storyId}/episode/{episodeId}/talk/{talkId}/click", null, null),
        ["favorite-set"] = new("POST", "/api/user/{userId}/story-favorite/{shareNo}/{storyType}/{storyId}", typeof(game::Sekai.StoryFavorite.PostShareStoryFavoriteRequest), "updatedResources"),
        ["favorite-delete"] = new("DELETE", "/api/user/{userId}/story-favorite/{shareNo}", typeof(game::Sekai.StoryFavorite.DeleteShareStoryFavoriteRequest), "updatedResources"),
        ["story-read"] = new("POST", "/api/user/{userId}/story/{storyType}/episode/{episodeId}", null, "obtainedResources"),
        ["story-release"] = new("POST", "/api/user/{userId}/story/{storyType}/episode/{episodeId}/cost", typeof(UserStoryRequest), "consumedResources"),
        ["story-log"] = new("POST", "/api/user/{userId}/story/{storyType}/episode/{episodeId}/log", typeof(UserStoryLogRequest), "userObtainResourceResults"),
        ["story-recommend"] = new("GET", "/api/user/{userId}/story/recommend", null, "userStoryRecommends", false),
        ["friend-story-favorites"] = new("GET", "/api/user/{userId}/story-favorite/friend/status/{storyType}", null, "friendStoryFavoriteStatuses", false),
        ["live-mission-receive"] = new("PUT", "/api/user/{userId}/mission/live_mission", typeof(UserMissionReceiveRequest), "obtainedRewards"),
        ["beginner-mission-receive"] = new("PUT", "/api/user/{userId}/mission/beginner_mission_v2", typeof(UserMissionReceiveRequest), "obtainedRewards")
    };

    private static readonly JsonSerializerOptions BodyOptions = new(DumpJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Path(OperationDefinition definition, ScenarioStep step, long userId)
    {
        var path = definition.Path.Replace("{userId}", userId.ToString(CultureInfo.InvariantCulture));
        foreach (var pair in step.Args)
        {
            if (pair.Key == "userId" || !path.Contains("{" + pair.Key + "}", StringComparison.Ordinal))
                throw new InvalidOperationException("场景包含无效路径参数。");
            if (pair.Key == "storyType")
            {
                if (!Enum.GetNames<StoryType>().Contains(pair.Value, StringComparer.Ordinal))
                    throw new InvalidOperationException("无效剧情类型。");
            }
            else if (pair.Key == "unit")
            {
                if (!Enum.GetNames<UnitType>().Any(u => u.ToUpperInvariant() == pair.Value))
                    throw new InvalidOperationException("组合路径参数须使用客户端枚举的大写名称。");
            }
            else if (pair.Key is "userLiveId" or "userChallengeLiveId" or "inheritId")
            {
                if (string.IsNullOrEmpty(pair.Value) || pair.Value.Length > 256 ||
                    pair.Value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
                    throw new InvalidOperationException("会话或引继 ID 必须是非空的单个路径标识。");
            }
            else if (!int.TryParse(pair.Value, CultureInfo.InvariantCulture, out var id) || id < 0)
                throw new InvalidOperationException("路径 ID 必须是非负整数。");
            path = path.Replace("{" + pair.Key + "}", Uri.EscapeDataString(pair.Value));
        }
        if (path.Contains('{')) throw new InvalidOperationException("场景缺少路径参数。");
        foreach (var (name, value) in step.Query)
        {
            string encoded;
            if (definition.BooleanQueries?.Contains(name, StringComparer.Ordinal) == true && bool.TryParse(value, out var parsed))
                encoded = parsed ? "true" : "false";
            else if (definition.RequiredIntegerQueries?.TryGetValue(name, out var minimum) == true &&
                     int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= minimum)
                encoded = number.ToString(CultureInfo.InvariantCulture);
            else throw new InvalidOperationException("查询参数未支持或值不符合要求。");
            path += (path.Contains('?') ? "&" : "?") + Uri.EscapeDataString(name) + "=" + encoded;
        }
        foreach (var required in definition.RequiredIntegerQueries?.Keys ?? Enumerable.Empty<string>())
            if (!step.Query.ContainsKey(required)) throw new InvalidOperationException("缺少必填的整数查询参数。");
        foreach (var required in definition.RequiredIntegerListQueries ?? [])
            if (!step.QueryLists.ContainsKey(required)) throw new InvalidOperationException("缺少必填的列表查询参数。");
        foreach (var (name, values) in step.QueryLists)
        {
            if (definition.RequiredIntegerListQueries?.Contains(name, StringComparer.Ordinal) != true ||
                values == null || values.Length == 0 || values.Any(v => v <= 0))
                throw new InvalidOperationException("列表查询参数必须是该操作支持的非空正整数列表。");
            foreach (var value in values)
                path += (path.Contains('?') ? "&" : "?") + Uri.EscapeDataString(name) + "=" + value.ToString(CultureInfo.InvariantCulture);
        }
        return path;
    }

    public static byte[]? EncodeBody(OperationDefinition definition, JsonObject? body)
    {
        if (definition.RequestType == null)
        {
            if (body != null) throw new InvalidOperationException("该操作不接受请求体。");
            return null;
        }
        if (body == null)
        {
            if (definition.OptionalBody) return null;
            throw new InvalidOperationException("该操作需要请求体。");
        }
        var value = JsonSerializer.Deserialize(body.ToJsonString(), definition.RequestType, BodyOptions)
            ?? throw new InvalidOperationException("请求体为空。");
        return DumpSerializer.SerializeObject(value);
    }
}
