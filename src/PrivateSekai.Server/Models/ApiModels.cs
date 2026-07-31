using MessagePack;

namespace PrivateSekai.Models;

/// <summary>
/// 各 API 端点的请求/响应模型
/// 对应 SekaiReverse 中的同名类
/// </summary>
/// 
// ===================== Auth =====================

[MessagePackObject]
public class UserAuthRequest
{
    [Key("credential")] public string? credential;
    [Key("deviceId")] public string? deviceId;
    [Key("authTriggerType")] public string? authTriggerType;
}

[MessagePackObject]
public class UserAuthResponse
{
    [Key("sessionToken")] public string? sessionToken;
    [Key("appVersion")] public string? appVersion;
    [Key("removeAssetVersion")] public string? removeAssetVersion;
    [Key("dataVersion")] public string? dataVersion;
    [Key("assetVersion")] public string? assetVersion;
    [Key("multiPlayVersion")] public string? multiPlayVersion;
    [Key("assetHash")] public string? assetHash;
    [Key("appVersionStatus")] public string? appVersionStatus;
    [Key("updatedResources")] public SuiteUser? updatedResources;
    [Key("isStreamingVirtualLiveForceOpenUser")] public bool isStreamingVirtualLiveForceOpenUser;
    [Key("deviceId")] public string? deviceId;
    [Key("suiteMasterSplitPath")] public string[]? suiteMasterSplitPath;
    [Key("obtainedBondsRewardIds")] public int[]? obtainedBondsRewardIds;
}

// ===================== System =====================

[MessagePackObject]
public class SystemResponse
{
    [Key("serverDate")] public long serverDate;
    [Key("profile")] public string? profile;
    [Key("maintenanceStatus")] public string? maintenanceStatus;
    [Key("timezone")] public string? timezone;
    [Key("appVersions")] public AppVersionInfo[]? appVersions;
}

[MessagePackObject]
public class AppVersionInfo
{
    [Key("systemProfile")] public string? systemProfile;
    [Key("appVersion")] public string? appVersion;
    [Key("multiPlayVersion")] public string? multiPlayVersion;
    [Key("assetVersion")] public string? assetVersion;
    [Key("appVersionStatus")] public string? appVersionStatus;
}

// ===================== Login / Registration =====================

[MessagePackObject]
public class UserAPIResponse
{
    [Key("userRegistration")] public UserRegistration? userRegistration;
    [Key("credential")] public string? credential;
    [Key("updatedResources")] public SuiteUser? updatedResources;
}

// ===================== Home =====================

[MessagePackObject]
public class HomeRefreshRequest
{
    [Key("refreshableTypes")] public string[]? refreshableTypes;
}

[MessagePackObject]
public class InformationResponse
{
    [Key("informations")] public UserNews[]? informations;
}

// ===================== Appeal =====================

[MessagePackObject]
public class UserAppealRequest
{
    [Key("appealIds")] public int[]? appealIds;
}

// ===================== Tutorial =====================

[MessagePackObject]
public class UserTutorialRequest
{
    [Key("tutorialStatus")] public string? tutorialStatus;
}

[MessagePackObject]
public class UserNameRequest
{
    [Key("userGamedata")] public UserGamedata? userGamedata;
}

// ===================== Profile =====================

[MessagePackObject]
public class UserProfileRequest
{
    [Key("userId")] public long userId;
    [Key("word")] public string? word;
    [Key("honorId1")] public int? honorId1;
    [Key("honorId2")] public int? honorId2;
    [Key("honorId3")] public int? honorId3;
    [Key("twitterId")] public string? twitterId;
    [Key("profileImageType")] public string? profileImageType;
    [Key("profileImageId")] public int? profileImageId;
}

// ===================== Present =====================

[MessagePackObject]
public class UserPresentRequest
{
    [Key("presentIds")] public string[]? presentIds;
}

[MessagePackObject]
public class UserPresentReceiveResponse
{
    [Key("updatedResources")] public SuiteUser? updatedResources;
    [Key("receivedUserPresents")] public List<UserPresentData>? receivedUserPresents;
}

[MessagePackObject]
public class UserPresentHistoriesResponse
{
    [Key("userPresentHistories")] public List<UserPresentHistoryData>? userPresentHistories;
}

// ===================== Topic =====================

[MessagePackObject]
public class TopicResponse
{
    [Key("updatedResources")] public SuiteUser? updatedResources;
}

// ===================== Card Exchange =====================

[MessagePackObject]
public class UserCardExchangeRequest
{
    [Key("userCards")] public UserCard[]? userCards;
}

[MessagePackObject]
public class UserCardExchangeResponse
{
    [Key("updatedResources")] public SuiteUser? updatedResources;
}

// ===================== Card Training =====================

[MessagePackObject]
public class UserCardPracticeTicketRequest
{
    [Key("costs")] public UserResource[]? costs;
}

[MessagePackObject]
public class UserCardPracticeTicketResponse
{
    [Key("updateExpResult")] public UpdateExpResult? updateExpResult;
    [Key("updatedResources")] public SuiteUser? updatedResources;
}

[MessagePackObject]
public class UserCardMasterLessonRequest
{
    [Key("masterLessonCostIds")] public List<int>? masterLessonCostIds;
}

[MessagePackObject]
public class UserCardMasterLessonResponse
{
    [Key("obtainedRewards")] public UserMasterLessonReward[]? obtainedRewards;
    [Key("updatedResources")] public SuiteUser? updatedResources;
}

[MessagePackObject]
public class UserMasterLessonReward
{
    [Key("masterLessonRewardId")] public int masterLessonRewardId;
    [Key("obtainRewards")] public UserResource[]? obtainRewards;
}

[MessagePackObject]
public class UserCardSpecialTrainingRequest
{
    [Key("specialTrainingStatus")] public string? specialTrainingStatus;
}

[MessagePackObject]
public class UserCardDefaultImageRequest
{
    [Key("defaultImage")] public string? defaultImage;
}

// ===================== Mission =====================

[MessagePackObject]
public class UserMissionReceiveRequest
{
    [Key("missionIds")] public int[]? missionIds;
    [Key("eventMissionSelectableRewardId")] public int eventMissionSelectableRewardId;
    [Key("isClosedEventMissionSelectableReward")] public bool isClosedEventMissionSelectableReward;
}

[MessagePackObject]
public class UserMissionReceiveResponse
{
    [Key("updatedResources")] public SuiteUser? updatedResources;
    [Key("obtainedRewards")] public UserResource[]? obtainedRewards;
}

// ===================== Story =====================

[MessagePackObject]
public class UserStoryResponse
{
    [Key("updatedResources")] public SuiteUser? updatedResources;
    [Key("obtainedResources")] public UserResource[]? obtainedResources;
}

[MessagePackObject]
public class UserStoryRequest
{
    [Key("cardEpisodeReleaseCostType")] public string? cardEpisodeReleaseCostType;
}

[MessagePackObject]
public class UserStoryCostResponse
{
    [Key("updatedResources")] public SuiteUser? updatedResources;
    [Key("consumedResources")] public UserResource[]? consumedResources;
}

[MessagePackObject]
public class UserStoryLogRequest
{
    [Key("noSkip")] public bool noSkip;
    [Key("useSkip")] public bool useSkip;
    [Key("autoFinish")] public bool autoFinish;
    [Key("useAuto")] public bool useAuto;
    [Key("fastForward")] public bool fastForward;
    [Key("voice")] public bool voice;
    [Key("numPages")] public int numPages;
    [Key("continuousPlayStart")] public bool continuousPlayStart;
    [Key("playMusicVideo")] public bool playMusicVideo;
    [Key("musicVocalId")] public int musicVocalId;
    [Key("musicCategoryName")] public string? musicCategoryName;
    [Key("musicVideoNoSkip")] public bool musicVideoNoSkip;
    [Key("userStoryMusicPlays")] public UserStoryMusicPlay[]? userStoryMusicPlays;
}

[MessagePackObject]
public class UserStoryMusicPlay
{
    [Key("musicId")] public int musicId;
    [Key("musicTrackType")] public string? musicTrackType;
}

[MessagePackObject]
public class UserStoryLogResponse
{
    [Key("updatedResources")] public SuiteUser? updatedResources;
    [Key("userObtainResourceResults")] public UserObtainResourceResult[]? userObtainResourceResults;
}

[MessagePackObject]
public class UserObtainResourceResult
{
    [Key("obtainReason")] public string? obtainReason;
    [Key("userResources")] public UserResource[]? userResources;
}

[MessagePackObject]
public class UserStoryRecommend
{
    [Key("storyType")] public string? storyType;
    [Key("storyId")] public int storyId;
    [Key("reason")] public string? reason;
    [Key("category")] public string? category;
    [Key("seq")] public int seq;
}

[MessagePackObject]
public class UserStoryRecommendResponse
{
    [Key("userStoryRecommends")] public UserStoryRecommend[]? userStoryRecommends;
}

[MessagePackObject]
public class StoryFavoriteFriendStatusResponse
{
    [Key("friendStoryFavoriteStatuses")] public object[]? friendStoryFavoriteStatuses;
}

[MessagePackObject]
public class StoryEpisodeBookmarkResponse
{
    [Key("userStoryEpisodeBookmarks")] public object[]? userStoryEpisodeBookmarks;
    [Key("updatedResources")] public SuiteUser? updatedResources;
}

// ===================== Inherit =====================

[MessagePackObject]
public class RestrictInfoResponse
{
    [Key("isRestrictDeviceTransfer")] public bool isRestrictDeviceTransfer;
}

[MessagePackObject]
public class UserInheritRequest
{
    [Key("password")] public string? password;
}

[MessagePackObject]
public class UserInheritSetResponse
{
    [Key("updatedResources")] public SuiteUser? updatedResources;
    [Key("userInherit")] public UserInherit? userInherit;
}

[MessagePackObject]
public class InheritExecuteResponse
{
    [Key("afterUserGamedata")] public UserGamedata? afterUserGamedata;
    [Key("userEventDeviceTransferRestrict")] public RestrictInfoResponse? userEventDeviceTransferRestrict;
    [Key("credential")] public string? credential;
}
