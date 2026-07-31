namespace PrivateSekai.Models.Master;

public sealed class MasterCard
{
    public int id { get; set; }
    public string? cardRarityType { get; set; }
}

public sealed class MasterCardRarity
{
    public string? cardRarityType { get; set; }
    public int maxLevel { get; set; }
    public int? trainingMaxLevel { get; set; }
    public int maxSkillLevel { get; set; }
}

public sealed class MasterCardExchangeResource
{
    public string? cardRarityType { get; set; }
    public int seq { get; set; }
    public int resourceBoxId { get; set; }
}

public sealed class MasterCardEpisode
{
    public int id { get; set; }
    public int seq { get; set; }
    public int cardId { get; set; }
    public int[]? rewardResourceBoxIds { get; set; }
    public MasterCardEpisodeCost[]? costs { get; set; }
    public string? cardEpisodePartType { get; set; }
}

public sealed class MasterCardEpisodeCost
{
    public string? resourceType { get; set; }
    public int resourceId { get; set; }
    public int quantity { get; set; }
}

public sealed class MasterConfigRow
{
    public string? configKey { get; set; }
    public string? value { get; set; }
}

public sealed class MasterLevel
{
    public int id { get; set; }
    public string? levelType { get; set; }
    public int level { get; set; }
    public int totalExp { get; set; }
}

public sealed class MasterPracticeTicket
{
    public int id { get; set; }
    public int exp { get; set; }
}

public sealed class MasterLesson
{
    public string? cardRarityType { get; set; }
    public int masterRank { get; set; }
    public MasterLessonCost[]? costs { get; set; }
}

public sealed class MasterLessonCost
{
    public int id { get; set; }
    public string? resourceType { get; set; }
    public int resourceId { get; set; }
    public int quantity { get; set; }
}

public sealed class MasterLessonReward
{
    public int id { get; set; }
    public int cardId { get; set; }
    public int masterRank { get; set; }
    public int resourceBoxId { get; set; }
}

public sealed class MasterBeginnerMissionV2
{
    public int id { get; set; }
    public int requirement { get; set; }
    public MasterMissionReward[]? rewards { get; set; }
}

public sealed class MasterMissionReward
{
    public int resourceBoxId { get; set; }
}

public sealed class MasterCardCostume3D
{
    public int cardId { get; set; }
    public int costume3dId { get; set; }
}

public sealed class MasterGacha
{
    public int id { get; set; }
    public int gachaCeilItemId { get; set; }
    public MasterGachaBehavior[]? gachaBehaviors { get; set; }
    public List<MasterGachaDetail>? gachaDetails { get; set; }
    public List<MasterGachaCardRarityRate>? gachaCardRarityRates { get; set; }
}

public sealed class MasterGachaBehavior
{
    public int id { get; set; }
    public int spinCount { get; set; }
    public string? gachaBehaviorType { get; set; }
    public string? resourceCategory { get; set; }
    public string? costResourceType { get; set; }
    public int costResourceId { get; set; }
    public int costResourceQuantity { get; set; }
}

public sealed class MasterGachaDetail
{
    public int cardId { get; set; }
    public int weight { get; set; }
}

public sealed class MasterGachaCardRarityRate
{
    public string? lotteryType { get; set; }
    public string? cardRarityType { get; set; }
    public double rate { get; set; }
}

public sealed class MasterGachaCeilItem
{
    public int id { get; set; }
    public int gachaId { get; set; }
}

public sealed class MasterGachaCeilExchangeSummary
{
    public int id { get; set; }
    public MasterGachaCeilExchange[]? gachaCeilExchanges { get; set; }
}

public sealed class MasterGachaCeilExchange
{
    public int id { get; set; }
    public int resourceBoxId { get; set; }
    public int exchangeLimit { get; set; }
    public MasterGachaCeilExchangeCost? gachaCeilExchangeCost { get; set; }
    public MasterGachaCeilExchangeSubstituteCost[]? gachaCeilExchangeSubstituteCosts { get; set; }
}

public sealed class MasterGachaCeilExchangeCost
{
    public string? resourceType { get; set; }
    public int resourceId { get; set; }
    public int gachaCeilItemId { get; set; }
    public int quantity { get; set; }
}

public sealed class MasterGachaCeilExchangeSubstituteCost
{
    public int id { get; set; }
    public string? resourceType { get; set; }
    public int resourceId { get; set; }
    public int substituteQuantity { get; set; }
}

public sealed class MasterShopItem
{
    public int id { get; set; }
    public int shopId { get; set; }
    public int resourceBoxId { get; set; }
    public MasterShopItemCostEntry[]? costs { get; set; }
}

public sealed class MasterShopItemCostEntry
{
    public MasterShopItemCost? cost { get; set; }
}

public sealed class MasterShopItemCost
{
    public string? resourceType { get; set; }
    public int resourceId { get; set; }
    public int quantity { get; set; }
}

public sealed class MasterResourceBox
{
    public string? resourceBoxPurpose { get; set; }
    public int id { get; set; }
    public MasterResourceBoxDetail[]? details { get; set; }
}

public sealed class MasterResourceBoxDetail
{
    public string? resourceType { get; set; }
    public int resourceId { get; set; }
    public int resourceLevel { get; set; }
    public int resourceQuantity { get; set; }
}

public sealed class MasterBoostRow
{
    public int id { get; set; }
    public int costBoost { get; set; }
    public bool isEventOnly { get; set; }
    public int expRate { get; set; }
    public int rewardRate { get; set; }
    public int livePointRate { get; set; }
    public int eventPointRate { get; set; }
    public int bondsExpRate { get; set; }
}

public sealed class MasterMusicDifficulty
{
    public int id { get; set; }
    public string? musicDifficulty { get; set; }
    public int totalNoteCount { get; set; }
    public int playLevel { get; set; }
}

public sealed class MasterMusicAchievement
{
    public int id { get; set; }
    public string? musicAchievementType { get; set; }
    public string? musicAchievementTypeValue { get; set; }
    public string? musicDifficultyType { get; set; }
    public int resourceBoxId { get; set; }
}

public sealed class MasterPlayLevelScore
{
    public string? liveType { get; set; }
    public int playLevel { get; set; }
    public int s { get; set; }
    public int a { get; set; }
    public int b { get; set; }
    public int c { get; set; }
}

public sealed class MasterMusicVocal
{
    public int id { get; set; }
    public int musicId { get; set; }
    public int releaseConditionId { get; set; }
}

public sealed class MasterMysekaiTool
{
    public int id { get; set; }
    public int maxDurability { get; set; }
}

public sealed class MasterLiveMissionPass
{
    public int id { get; set; }
    public int liveMissionPeriodId { get; set; }
}
