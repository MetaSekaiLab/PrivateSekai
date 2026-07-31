using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserLiveRequest
{
    [Key("musicId")] public int musicId;
    [Key("musicDifficultyId")] public int musicDifficultyId;
    [Key("musicVocalId")] public int musicVocalId;
    [Key("deckId")] public int deckId;
    [Key("boostCount")] public int boostCount;
    [Key("isAuto")] public bool isAuto;
    [Key("musicCategoryName")] public string? musicCategoryName;
    [Key("customMusicScoreId")] public long? customMusicScoreId;
}

[MessagePackObject]
public class UpdatedResources
{
    [Key("userEventBreakTime")] public UserEventBreakTime? userEventBreakTime;
}

[MessagePackObject]
public class UserLive
{
    [Key("updatedResources")] public SuiteUser? updatedResources;
    [Key("userLiveId")] public string? userLiveId;
    [Key("skills")] public IngameLotterySkill[]? skills;
    [Key("comboCutins")] public IngameComboCutin[]? comboCutins;
    [Key("isInBreakTime")] public bool isInBreakTime;
}

[MessagePackObject]
public class IngameLotterySkill
{
    [Key("seq")] public int seq;
    [Key("cardId")] public int cardId;
    [Key("relationCardId")] public int? relationCardId;
    [Key("ingameCutinCharacterId")] public int? ingameCutinCharacterId;
}

[MessagePackObject]
public class IngameComboCutin
{
    [Key("cardId1")] public int cardId1;
    [Key("cardId2")] public int cardId2;
    [Key("ingameCutinCharacterId")] public int ingameCutinCharacterId;
}

[MessagePackObject]
public class UserLiveClearRequest
{
    [Key("score")] public int score;
    [Key("perfectCount")] public int perfectCount;
    [Key("greatCount")] public int greatCount;
    [Key("goodCount")] public int goodCount;
    [Key("badCount")] public int badCount;
    [Key("missCount")] public int missCount;
    [Key("maxCombo")] public int maxCombo;
    [Key("life")] public int life;
    [Key("tapCount")] public int tapCount;
    [Key("musicCategoryName")] public string? musicCategoryName;
    [Key("isMirrored")] public bool isMirrored;
    [Key("ingameCutinCharacterArchiveVoiceGroupIds")] public List<int>? ingameCutinCharacterArchiveVoiceGroupIds;
}

[MessagePackObject]
public class UserLiveClearResponse
{
    [Key("updatedResources")] public SuiteUser? updatedResources;
    [Key("scoreRank")] public string? scoreRank;
    [Key("score")] public int score;
    [Key("perfectCount")] public int perfectCount;
    [Key("greatCount")] public int greatCount;
    [Key("goodCount")] public int goodCount;
    [Key("badCount")] public int badCount;
    [Key("missCount")] public int missCount;
    [Key("maxCombo")] public int maxCombo;
    [Key("highScoreFlg")] public bool highScoreFlg;
    [Key("fullComboFlg")] public bool fullComboFlg;
    [Key("fullPerfectFlg")] public bool fullPerfectFlg;
    [Key("userExpResult")] public UpdateExpResult? userExpResult;
    [Key("deckCardExpResults")] public DeckCardUpdateExpResult[]? deckCardExpResults;
    [Key("unitExpResults")] public UnitUpdateExpResult[]? unitExpResults;
    [Key("userDeck")] public UserDeck? userDeck;
    [Key("userMusicAchievements")] public UserMusicAchievement[]? userMusicAchievements;
    [Key("scoreRankRewards")] public UserResource[]? scoreRankRewards;
    [Key("playerRankRewards")] public UserResource[]? playerRankRewards;
    [Key("limitedTermScoreRankRewards")] public LimitedTermScoreRankRewardResult[]? limitedTermScoreRankRewards;
    [Key("musicAchievementRewards")] public UserResource[]? musicAchievementRewards;
    [Key("boost")] public MasterBoost? boost;
    [Key("beforeEventPoint")] public int beforeEventPoint;
    [Key("afterEventPoint")] public int afterEventPoint;
    [Key("beforeEventItemQuantity")] public int beforeEventItemQuantity;
    [Key("afterEventItemQuantity")] public int afterEventItemQuantity;
    [Key("beforeWorldBloomChapterPoint")] public int? beforeWorldBloomChapterPoint;
    [Key("afterWorldBloomChapterPoint")] public int? afterWorldBloomChapterPoint;
    [Key("worldBloomChapterNo")] public int? worldBloomChapterNo;
    [Key("isPreliminaryTournament")] public bool isPreliminaryTournament;
    [Key("bondsUpdateExpResults")] public UserBondsUpdateExpResult[]? bondsUpdateExpResults;
    [Key("userEventDeviceTransferRestrict")] public UserRestrictInfo? userEventDeviceTransferRestrict;
    [Key("userLivePoint")] public UserLivePoint? userLivePoint;
    [Key("isEventMaintenance")] public bool isEventMaintenance;
    [Key("isInBreakTime")] public bool isInBreakTime;
}

[MessagePackObject]
public class UpdateExpResult
{
    [Key("beforeTotalExp")] public int beforeTotalExp;
    [Key("afterTotalExp")] public int afterTotalExp;
    [Key("beforeExp")] public int beforeExp;
    [Key("afterExp")] public int afterExp;
    [Key("beforeLevel")] public int beforeLevel;
    [Key("afterLevel")] public int afterLevel;
}

[MessagePackObject]
public class DeckCardUpdateExpResult
{
    [Key("index")] public int index;
    [Key("expResult")] public UpdateExpResult? expResult;
}

[MessagePackObject]
public class UnitUpdateExpResult
{
    [Key("unit")] public string? unit;
    [Key("expResult")] public UpdateExpResult? expResult;
}

[MessagePackObject]
public class UserResource
{
    [Key("resourceType")] public string? resourceType;
    [Key("resourceId")] public int resourceId;
    [Key("resourceLevel")] public int resourceLevel;
    [Key("quantity")] public int quantity;
}

[MessagePackObject]
public class LimitedTermScoreRankRewardResult
{
    [Key("obtainedRewards")] public UserResource[]? obtainedRewards;
    [Key("scoreRankRewardType")] public string? scoreRankRewardType;
}

[MessagePackObject]
public class MasterBoost
{
    [Key("id")] public int id;
    [Key("costBoost")] public int costBoost;
    [Key("isEventOnly")] public bool isEventOnly;
    [Key("expRate")] public int expRate;
    [Key("rewardRate")] public int rewardRate;
    [Key("livePointRate")] public int livePointRate;
    [Key("eventPointRate")] public int eventPointRate;
    [Key("bondsExpRate")] public int bondsExpRate;
}

[MessagePackObject]
public class UserBondsUpdateExpResult
{
    [Key("bondsGroupId")] public int bondsGroupId;
    [Key("expResult")] public UpdateExpResult? expResult;
    [Key("grantedBondsRewards")] public List<UserResource>? grantedBondsRewards;
}

[MessagePackObject]
public class UserRestrictInfo
{
    [Key("isRestrictDeviceTransfer")] public bool isRestrictDeviceTransfer;
    [Key("restrictEndAt")] public long? restrictEndAt;
    [Key("restTransferCount")] public int? restTransferCount;
    [Key("isWorldBloomChapter")] public bool? isWorldBloomChapter;
    [Key("restrictRank")] public long? restrictRank;
    [Key("gameCharacterId")] public int? gameCharacterId;
}

[MessagePackObject]
public class UserLivePoint
{
    [Key("addNormalProgress")] public int addNormalProgress;
    [Key("addDailyBonusProgress")] public int addDailyBonusProgress;
    [Key("livePointBonusRemaining")] public int livePointBonusRemaining;
    [Key("liveMissionPeriodId")] public int liveMissionPeriodId;
}

[MessagePackObject]
public class UserLiveCharacterArchiveVoiceLiveResultRequest
{
    [Key("liveResultCharacterArchiveVoiceGroupId")] public int liveResultCharacterArchiveVoiceGroupId;
    [Key("liveType")] public string? liveType;
    [Key("userLiveId")] public string? userLiveId;
}

[MessagePackObject]
public class UserLiveCharacterArchiveVoiceLiveResultResponse
{
    [Key("updatedResources")] public SuiteUser? updatedResources;
}
