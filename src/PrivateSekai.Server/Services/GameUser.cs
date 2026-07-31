using System.Reflection;
using MessagePack;
using PrivateSekai.Config;
using PrivateSekai.Crypto;
using PrivateSekai.Models;
using PrivateSekai.Models.Master;
using PrivateSekai.Services.Master;

namespace PrivateSekai.Services;

/// <summary>
/// 对应 Python game/user.py 的 User(Box) + 所有 Mixin
/// 内部数据使用强类型 SuiteUser
/// </summary>
public class GameUser
{
    private static MasterDataManager Master => MasterDataManager.Instance;

    /// <summary>
    /// 广为人知的 Suite 数据
    /// </summary>
    public SuiteUser Data { get; private set; }

    /// <summary>
    /// 不随 GetSuiteUserData 导出的私有数据
    /// </summary>
    public NotSuiteData NotSuite { get; private set; }

    /// <summary>反射缓存：MessagePack Key → SuiteUser FieldInfo</summary>
    private static readonly Dictionary<string, FieldInfo> SuiteUserFields =
        typeof(SuiteUser).GetFields()
            .Where(f => f.GetCustomAttribute<KeyAttribute>() != null)
            .ToDictionary(
                f => f.GetCustomAttribute<KeyAttribute>()!.StringKey!,
                f => f
            );

    private static readonly Dictionary<string, string> SuiteUserFieldKeys =
        typeof(SuiteUser).GetFields()
            .Where(f => f.GetCustomAttribute<KeyAttribute>()?.StringKey != null)
            .ToDictionary(
                f => f.Name,
                f => f.GetCustomAttribute<KeyAttribute>()!.StringKey!
            );

    /// <summary>
    /// 无实义
    /// </summary>
    private const long TemplatePlaceholderTimestamp = 1188486000000L; // 2007-08-30T15:00:00Z
    
    /// <summary>
    /// TODO: 移动到新手教程结束(或其他某个正确的位置)后直接解锁，而非每次刷新
    /// </summary>
    private static readonly Dictionary<int, int[]> FixedShopActionSetsByArea = new()
    {
        [3] = [4, 384, 2002, 2005],
        [4] = [3, 838, 2001, 2006]
    };

    private const string CardEpisodeReleaseCostTypeCommonMaterial = "common_material";
    private const string CardEpisodeReleaseCostTypeTicket = "card_episode_release_ticket";
    private const string CardEpisodeReleaseTicketMaterialIdConfig = "card_episode_release_ticket_material_id";
    private const string CardEpisodeReleaseCostQuantityConfig = "card_episode_release_cost_quantity";
    private const string BeginnerMissionV2Type = "beginner_mission_v2";
    
    public GameUser(SuiteUser? data = null)
    {
        Data = data ?? new SuiteUser();
        NotSuite = new NotSuiteData();
    }

    public long GetUserId() =>
        Data.userRegistration?.userId ?? 0;
    
    public void InitAllUserId(long newUserId)
    {
        if (Data.userRegistration != null)
        {
            Data.userRegistration.userId = newUserId;
            Data.userRegistration.signature = JwtSignature.GenUserSignature(newUserId);
        }
        if (Data.userGamedata != null) Data.userGamedata.userId = newUserId;
        if (Data.userProfile != null) Data.userProfile.userId = newUserId;

        if (Data.userCards != null)
            foreach (var c in Data.userCards) c.userId = newUserId;
        if (Data.userDecks != null)
            foreach (var d in Data.userDecks) d.userId = newUserId;
        if (Data.userUnits != null)
            foreach (var u in Data.userUnits) u.userId = newUserId;
        if (Data.unreadUserTopics != null)
            foreach (var t in Data.unreadUserTopics) t.userId = newUserId;
        if (Data.userMaterialExchanges != null)
            foreach (var m in Data.userMaterialExchanges) m.userId = newUserId;
        if (Data.userGachaCeilExchanges != null)
            foreach (var g in Data.userGachaCeilExchanges) g.userId = newUserId;
        if (Data.userCharacterMissionV2Statuses != null)
            foreach (var s in Data.userCharacterMissionV2Statuses) s.userId = newUserId;
    }

    public void InitAllUserTime(long currentTime)
    {
        Data.now = currentTime;
        Data.userRegistration?.registeredAt = (ulong)currentTime;
        Data.userBoost?.recoveryAt = (ulong)currentTime;

        if (Data.userCards != null)
            foreach (var card in Data.userCards) card.createdAt = currentTime;

        if (Data.userCostume3dStatuses != null)
            foreach (var status in Data.userCostume3dStatuses) status.obtainedAt = currentTime;

        if (Data.userReleaseConditions != null)
            foreach (var cond in Data.userReleaseConditions) cond.createdAt = currentTime;
    }
    
    public SuiteUser GetSuiteUserData()
    {
        var now = UserManager.Now;
        Data.now = now;
        NormalizeUserEventBreakTime(now);
        EnsureShopAreaActionSets();

        return Data;
    }
    
    public SuiteUser GetRefreshData(HashSet<string>? deleteRtypes = null)
    {
        var now = UserManager.Now;
        NormalizeUserEventBreakTime(now);

        var baseRtypes = new HashSet<string>
        {
            "now", "refreshableTypes", "userPresents", "unreadUserTopics",
            "userHomeBanners", "userMaterialExchanges", "userGachaCeilExchanges",
            "userRankMatchResult", "userViewableAppeal",
            "userBillingRefunds", "userUnprocessedOrders", "userInformations"
        };

        if (Data.refreshableTypes != null)
        {
            foreach (var r in Data.refreshableTypes)
                baseRtypes.Add(r);
        }

        if (deleteRtypes != null)
            baseRtypes.ExceptWith(deleteRtypes);

        // 清空 refreshableTypes
        Data.refreshableTypes = [];

        // 通过反射选择性拷贝字段
        var result = new SuiteUser();
        foreach (var rtype in baseRtypes)
        {
            if (SuiteUserFields.TryGetValue(rtype, out var field))
            {
                var value = field.GetValue(Data);
                if (value != null)
                    field.SetValue(result, value);
            }
        }
        result.now = now;

        return result;
    }

    public SuiteUser GetSuiteUserParts(IEnumerable<string>? partNames)
    {
        var now = UserManager.Now;
        NormalizeUserEventBreakTime(now);

        var result = new SuiteUser
        {
            now = now,
            refreshableTypes = []
        };

        if (partNames == null)
        {
            return GetRefreshData();
        }

        foreach (var partName in partNames)
        {
            switch (partName)
            {
                case "user_event_break_time":
                    result.userEventBreakTime = Data.userEventBreakTime;
                    break;
                case "user_friend":
                    result.userFriends = Data.userFriends;
                    break;
                default:
                    return GetRefreshData();
            }
        }
        
        return result;
    }

    private void NormalizeUserEventBreakTime(long now)
    {
        var userEventBreakTime = Data.userEventBreakTime;
        if (userEventBreakTime == null)
            return;

        if (userEventBreakTime.lastDecreaseAt > 0 &&
            userEventBreakTime.lastDecreaseAt != TemplatePlaceholderTimestamp)
        {
            return;
        }

        userEventBreakTime.lastDecreaseAt = now;
        userEventBreakTime.playTimeUsedMillis = 0;
    }
    
    public void UpdateUserName(string newName)
    {
        Data.userGamedata?.name = newName;
    }

    /// <summary>
    /// 标记需更新字段，将在获取 UpdateResource 时将对应数据段返回给客户端，TODO: 考虑使用标记位而非字段表
    /// </summary>
    /// <param name="suiteUserFieldName"></param>
    /// <exception cref="ArgumentException"></exception>
    public void UpdateRefreshableType(string suiteUserFieldName)
    {
        if (!SuiteUserFieldKeys.TryGetValue(suiteUserFieldName, out var rtype))
            throw new ArgumentException($"Unknown SuiteUser field: {suiteUserFieldName}", nameof(suiteUserFieldName));

        AddRefreshableType(rtype);
    }

    public void UpdateRefreshableTypes(IEnumerable<string> suiteUserFieldNames)
    {
        foreach (var suiteUserFieldName in suiteUserFieldNames)
        {
            UpdateRefreshableType(suiteUserFieldName);
        }
    }
    

    private void AddRefreshableType(string rtype)
    {
        Data.refreshableTypes ??= [];
        if (!Data.refreshableTypes.Contains(rtype))
            Data.refreshableTypes.Add(rtype);
    }

    public void MarkAppealsViewed(int[]? appealIds)
    {
        if (appealIds == null || appealIds.Length == 0)
            return;

        var mergedIds = (Data.userViewableAppeal?.appealIds ?? [])
            .Concat(appealIds.Where(id => id > 0))
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        Data.userViewableAppeal = new ViewableAppeal
        {
            appealIds = mergedIds
        };
        UpdateRefreshableType(nameof(SuiteUser.userViewableAppeal));
    }

    public void RefreshAreaActionSets()
    {
        EnsureShopAreaActionSets();
        UpdateRefreshableType(nameof(SuiteUser.userAreas));
    }

    public void EnsureShopAreaActionSets()
    {
        if (Data.userAreas == null)
            return;

        foreach (var (areaId, actionSetIds) in FixedShopActionSetsByArea)
        {
            var area = Data.userAreas.FirstOrDefault(a => a.areaId == areaId);
            if (area == null)
                continue;

            area.userAreaStatus ??= new UserAreaStatus
            {
                areaId = areaId
            };
            area.userAreaStatus.status = UserAreaStatus.AREA_STATUS_RELEASED;

            var actionSets = (area.actionSets ?? []).ToList();
            foreach (var actionSetId in actionSetIds)
            {
                if (actionSets.Any(a => a.id == actionSetId))
                    continue;

                actionSets.Add(new UserActionSet
                {
                    id = actionSetId,
                    status = UserActionSet.ACTION_SET_STATUS_UNREAD
                });
            }

            area.actionSets = actionSets.ToArray();
        }
    }

    /// <summary>
    /// 阅读不同团体的初始剧情后，会解锁对应的角色卡牌，现仅只实现新手教程部分，未实现阅读其他初始剧情补发卡牌的功能 (TODO)
    /// </summary>
    private static readonly Dictionary<string, int[]> TutorialCardsByUnit = new()
    {
        ["light_sound_opening"]     = [1, 5, 9, 13, 81, 82, 89, 93, 97, 98, 101, 105],
        ["idol_opening"]            = [17, 21, 25, 29, 81, 83, 89, 90, 93, 97, 101, 105],
        ["street_opening"]          = [33, 37, 41, 45, 81, 84, 89, 93, 94, 97, 102, 105],
        ["theme_park_opening"]      = [49, 53, 57, 61, 81, 85, 89, 93, 97, 101, 105, 106],
        ["school_refusal_opening"]  = [65, 69, 73, 77, 81, 86, 89, 93, 97, 101, 105]
    };

    public void UpdateTutorialProgress(string newStatus)
    {
        if (Data.userTutorial == null) return;

        var oldStatus = Data.userTutorial.tutorialStatus;
        Data.userTutorial.tutorialStatus = newStatus;

        if (oldStatus != null && TutorialCardsByUnit.TryGetValue(oldStatus, out var cardIds))
        {
            foreach (var cardId in cardIds)
                AddCard(cardId);

            UpdateRefreshableTypes(new[]
            {
                nameof(SuiteUser.userCards),
                nameof(SuiteUser.userDecks),
                nameof(SuiteUser.userUnitEpisodeStatuses),
                nameof(SuiteUser.userCharacterMissionV2s),
                nameof(SuiteUser.userCharacterMissionV2Statuses),
                nameof(SuiteUser.userBeginnerMissionV2s),
                nameof(SuiteUser.userMissionStatuses),
                nameof(SuiteUser.userHonorMissions)
            });

        }

        if (newStatus == "end")
        {
            Data.userTutorial.tutorialEndAt = UserManager.Now;
        }

        UpdateRefreshableType(nameof(SuiteUser.userTutorial));
    }

    private static List<int> GetCardEpisodeIds(int cardId) => Master.GetCardEpisodeIds(cardId);

    public UserCard AddCard(int cardId)
    {
        var currentTime = UserManager.Now;
        var episodeIds = GetCardEpisodeIds(cardId);
        long userId = Data.userGamedata?.userId ?? 0;

        var newCard = new UserCard
        {
            userId = userId,
            cardId = cardId,
            level = 1,
            exp = 0,
            totalExp = 0,
            skillLevel = 1,
            skillExp = 0,
            totalSkillExp = 0,
            masterRank = 0,
            specialTrainingStatus = "not_doing",
            defaultImage = "original",
            duplicateCount = 0,
            createdAt = currentTime,
            episodes =
            [
                new UserCardEpisode
                {
                    cardEpisodeId = episodeIds[0],
                    scenarioStatus = "unread_before_scenario",
                    scenarioStatusReasons = [],
                    isNotSkipped = false
                },
                new UserCardEpisode
                {
                    cardEpisodeId = episodeIds[1],
                    scenarioStatus = "can_not_read",
                    scenarioStatusReasons = ["unread_before_scenario", "not_enough_release_condition"],
                    isNotSkipped = false
                }
            ]
        };

        Data.userCards ??= [];
        if (Data.userCards.All(c => c.cardId != cardId))
            Data.userCards.Add(newCard);

        UpdateRefreshableType(nameof(SuiteUser.userCards));
        return newCard;
    }

    public UserGachaResponse ExecuteGacha(int gachaId, int gachaBehaviorId, bool isPriorityUsePaidJewel)
    {
        var now = UserManager.Now;
        var gacha = Master.GetMasterGacha(gachaId);
        var behavior = Master.GetMasterGachaBehavior(gacha, gachaBehaviorId);
        var spinCount = GetGachaSpinCount(behavior, gachaBehaviorId);
        var behaviorType = behavior?.gachaBehaviorType;

        var consumedCosts = ConsumeGachaCost(behavior, spinCount, isPriorityUsePaidJewel);

        var drawnCardIds = DrawGachaCards(gacha, behaviorType, spinCount);
        var obtainPrizes = new List<UserGachaSpinObtainPrize>();
        foreach (var cardId in drawnCardIds)
        {
            var isNew = GrantGachaCard(cardId);
            var costume3ds = isNew ? GrantInitialCostumes(cardId, now) : [];
            obtainPrizes.Add(new UserGachaSpinObtainPrize
            {
                card = new UserResource
                {
                    resourceId = cardId,
                    resourceType = "card",
                    resourceLevel = 1,
                    quantity = 1
                },
                newFlg = isNew,
                gachaLotteryType = "normal",
                costume3d = costume3ds,
                cardExtra = []
            });
        }

        var userGacha = UpsertUserGacha(gachaId, gachaBehaviorId, now);
        var obtainGachaCeilItems = GrantGachaCeilItem(gacha, gachaId, spinCount);

        UpdateRefreshableTypes(new []
        {
            nameof(SuiteUser.userCharacterMissionV2s),
            nameof(SuiteUser.userCharacterMissionV2Statuses),
            nameof(SuiteUser.userHonorMissions)
        });

        return new UserGachaResponse
        {
            consumedCosts = consumedCosts,
            obtainPrizes = obtainPrizes.ToArray(),
            obtainGachaCeilItems = obtainGachaCeilItems,
            obtainGachaBonusItems = [],
            obtainGachaExtras = [],
            obtainGachaFreebies = [],
            userGacha = userGacha,
            updatedResources = GetRefreshData(),
            obtainCharacterAllBonuses = [],
            obtainCharacterRepeatedBonuses = []
        };
    }

    public void SaveRateChoiceGachaWish(UserRateChoiceGachaWishRequest request)
    {
        if (request.rateChoiceGachaDetails == null)
            return;

        Data.userRateChoiceGachaWishes ??= [];
        var wishes = Data.userRateChoiceGachaWishes
            .Where(w => w.gachaId != request.gachaId)
            .ToList();

        wishes.AddRange(request.rateChoiceGachaDetails.Select(detail => new UserRateChoiceGachaWish
        {
            gachaId = request.gachaId,
            gachaDetailId = detail.gachaDetailId,
            rateChoiceGachaWishId = detail.rateChoiceGachaWishId
        }));

        Data.userRateChoiceGachaWishes = wishes
            .OrderBy(w => w.gachaId)
            .ThenBy(w => w.rateChoiceGachaWishId)
            .ThenBy(w => w.gachaDetailId)
            .ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userRateChoiceGachaWishes));
    }

    public UserGachaCeilExchangeResponse ExchangeGachaCeilItem(UserGachaCeilExchangeRequest request)
    {
        var exchangeRequest = request.gachaCeilExchangeRequest;
        if (exchangeRequest == null || exchangeRequest.gachaExchangeId <= 0 || exchangeRequest.exchangeCount <= 0)
        {
            return new UserGachaCeilExchangeResponse
            {
                obtainUserResources = [],
                updatedResources = GetRefreshData()
            };
        }

        var exchange = Master.GetMasterGachaCeilExchange(exchangeRequest.gachaExchangeId);
        var resourceBoxId = exchange?.resourceBoxId ?? 0;
        var obtainResources = Master.BuildResourcesFromBox("gacha_ceil_exchange", resourceBoxId);

        var exchangeCount = exchangeRequest.exchangeCount;
        ConsumeGachaCeilExchangeCost(exchange?.gachaCeilExchangeCost, exchangeCount);
        ConsumeGachaCeilSubstituteCost(exchange, exchangeRequest);

        var exchangeId = exchangeRequest.gachaExchangeId;
        var exchangeLimit = exchange?.exchangeLimit ?? 0;
        UpsertUserGachaCeilExchange(exchangeId, exchangeLimit, exchangeCount);

        var obtained = new Dictionary<(string? Type, int Id, int Level), UserResource>();
        for (var i = 0; i < exchangeCount; i++)
        {
            foreach (var resource in obtainResources)
            {
                ApplyResource(resource);
                AddObtainedResource(obtained, resource);
            }
        }

        return new UserGachaCeilExchangeResponse
        {
            obtainUserResources = obtained.Values.ToArray(),
            updatedResources = GetRefreshData()
        };
    }

    private static int GetGachaSpinCount(MasterGachaBehavior? behavior, int gachaBehaviorId)
    {
        var spinCount = behavior?.spinCount ?? 0;
        if (spinCount > 0)
            return spinCount;

        return 1;
    }

    private UserResource[] ConsumeGachaCost(MasterGachaBehavior? behavior, int spinCount, bool isPriorityUsePaidJewel)
    {
        if (string.Equals(behavior?.resourceCategory, "free_resource", StringComparison.Ordinal))
            return [];

        var costResourceType = behavior?.costResourceType;
        var costResourceId = behavior?.costResourceId ?? 0;
        var costQuantity = behavior?.costResourceQuantity ?? 300 * spinCount;
        if (costQuantity <= 0 || string.IsNullOrEmpty(costResourceType))
            return [];

        return costResourceType switch
        {
            "jewel" =>
            [
                new UserResource
                {
                    resourceType = "jewel",
                    resourceLevel = 0,
                    quantity = ConsumeJewelForGacha(costQuantity, isPriorityUsePaidJewel)
                }
            ],
            "paid_jewel" =>
            [
                new UserResource
                {
                    resourceType = "paid_jewel",
                    resourceLevel = 0,
                    quantity = ConsumePaidJewelForGacha(costQuantity)
                }
            ],
            "gacha_ticket" =>
            [
                new UserResource
                {
                    resourceId = costResourceId,
                    resourceType = "gacha_ticket",
                    resourceLevel = 1,
                    quantity = ConsumeGachaTicketForGacha(costResourceId, costQuantity)
                }
            ],
            _ => []
        };
    }

    private int ConsumeJewelForGacha(int quantity, bool isPriorityUsePaidJewel)
    {
        Data.userChargedCurrency ??= new ChargedCurrency { paidUnitPrices = [] };

        if (isPriorityUsePaidJewel)
        {
            var paidCost = Math.Min(Math.Max(0, Data.userChargedCurrency.paid), quantity);
            Data.userChargedCurrency.paid -= paidCost;
            var remainingCost = quantity - paidCost;
            if (remainingCost > 0)
                Data.userChargedCurrency.free = Math.Max(0, Math.Max(0, Data.userChargedCurrency.free) - remainingCost);
        }
        else
        {
            var freeCost = Math.Min(Math.Max(0, Data.userChargedCurrency.free), quantity);
            Data.userChargedCurrency.free -= freeCost;
            var remainingCost = quantity - freeCost;
            if (remainingCost > 0)
                Data.userChargedCurrency.paid = Math.Max(0, Math.Max(0, Data.userChargedCurrency.paid) - remainingCost);
        }

        UpdateRefreshableType(nameof(SuiteUser.userChargedCurrency));
        return Math.Max(0, Data.userChargedCurrency.paid) + Math.Max(0, Data.userChargedCurrency.free);
    }

    private int ConsumePaidJewelForGacha(int quantity)
    {
        Data.userChargedCurrency ??= new ChargedCurrency { paidUnitPrices = [] };
        Data.userChargedCurrency.paid = Math.Max(0, Math.Max(0, Data.userChargedCurrency.paid) - quantity);
        UpdateRefreshableType(nameof(SuiteUser.userChargedCurrency));
        return Data.userChargedCurrency.paid;
    }

    private int ConsumeGachaTicketForGacha(int gachaTicketId, int quantity)
    {
        if (gachaTicketId <= 0 || quantity <= 0)
            return 0;

        Data.userGachaTickets ??= [];
        var tickets = Data.userGachaTickets.ToList();
        var ticket = tickets.FirstOrDefault(t => t.gachaTicketId == gachaTicketId);
        if (ticket == null)
        {
            ticket = new UserGachaTicket
            {
                userId = GetUserId(),
                gachaTicketId = gachaTicketId
            };
            tickets.Add(ticket);
        }

        ticket.quantity = Math.Max(0, ticket.quantity - quantity);
        Data.userGachaTickets = tickets.OrderBy(t => t.gachaTicketId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userGachaTickets));
        return ticket.quantity;
    }

    private static int[] DrawGachaCards(MasterGacha? gacha, string? behaviorType, int spinCount)
    {
        var cardIds = new int[spinCount];
        for (var i = 0; i < cardIds.Length; i++)
            cardIds[i] = DrawGachaCard(gacha);

        if (cardIds.Length == 0)
            return cardIds;

        if (string.Equals(behaviorType, "over_rarity_4_once", StringComparison.Ordinal) &&
            !cardIds.Any(IsRarity4OrHigher))
        {
            cardIds[^1] = DrawGachaCard(gacha, IsRarity4OrHigher);
        }
        else if (string.Equals(behaviorType, "over_rarity_3_once", StringComparison.Ordinal) &&
                 !cardIds.Any(IsRarity3OrHigher))
        {
            cardIds[^1] = DrawGachaCard(gacha, IsRarity3OrHigher);
        }

        return cardIds;
    }

    private static int DrawGachaCard(MasterGacha? gacha, Func<int, bool>? cardFilter = null)
    {
        var rarityType = DrawGachaRarity(gacha, cardFilter);
        return DrawGachaCardByRarity(gacha, rarityType, cardFilter);
    }

    private static string? DrawGachaRarity(MasterGacha? gacha, Func<int, bool>? cardFilter)
    {
        if (gacha?.gachaCardRarityRates == null)
            return null;

        var candidates = gacha.gachaCardRarityRates
            .Where(rate => rate.lotteryType == "normal")
            .Select(rate => new
            {
                RarityType = rate.cardRarityType,
                Rate = rate.rate
            })
            .Where(rate => !string.IsNullOrEmpty(rate.RarityType) &&
                           rate.Rate > 0 &&
                           HasGachaCardInRarity(gacha, rate.RarityType!, cardFilter))
            .ToArray();

        var totalRate = candidates.Sum(rate => rate.Rate);
        if (totalRate <= 0)
            return null;

        var selected = Random.Shared.NextDouble() * totalRate;
        double running = 0;
        foreach (var candidate in candidates)
        {
            running += candidate.Rate;
            if (selected < running)
                return candidate.RarityType;
        }

        return candidates[^1].RarityType;
    }

    private static bool HasGachaCardInRarity(MasterGacha? gacha, string rarityType, Func<int, bool>? cardFilter)
    {
        if (gacha?.gachaDetails == null)
            return false;

        foreach (var detail in gacha.gachaDetails)
        {
            if (detail.cardId <= 0 || cardFilter?.Invoke(detail.cardId) == false)
                continue;

            if (string.Equals(GetCardRarityType(detail.cardId), rarityType, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static int DrawGachaCardByRarity(MasterGacha? gacha, string? rarityType, Func<int, bool>? cardFilter)
    {
        if (gacha?.gachaDetails == null)
            return DrawFallbackCard(cardFilter);

        var candidates = gacha.gachaDetails
            .Select(detail => new
            {
                CardId = detail.cardId,
                Weight = detail.weight
            })
            .Where(detail => detail.CardId > 0 && detail.Weight > 0)
            .Where(detail => cardFilter?.Invoke(detail.CardId) != false)
            .Where(detail => string.IsNullOrEmpty(rarityType) ||
                             string.Equals(GetCardRarityType(detail.CardId), rarityType, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
            return DrawFallbackCard(cardFilter);

        var totalWeight = candidates.Sum(detail => (long)detail.Weight);
        if (totalWeight <= 0)
            return DrawFallbackCard(cardFilter);

        var selected = Random.Shared.NextInt64(totalWeight);
        long running = 0;
        foreach (var candidate in candidates)
        {
            running += candidate.Weight;
            if (selected < running)
                return candidate.CardId;
        }

        return DrawFallbackCard(cardFilter);
    }

    private static int DrawFallbackCard(Func<int, bool>? cardFilter)
    {
        var candidates = Master.GetMasterCards()
            .Select(card => card.id)
            .Where(cardId => cardId > 0 && !string.Equals(GetCardRarityType(cardId), "rarity_1", StringComparison.Ordinal))
            .Where(cardId => cardFilter?.Invoke(cardId) != false)
            .ToArray();

        return candidates.Length == 0 ? 1 : candidates[Random.Shared.Next(candidates.Length)];
    }

    private static bool IsRarity3OrHigher(int cardId) =>
        RarityRank(GetCardRarityType(cardId)) >= 3;

    private static bool IsRarity4OrHigher(int cardId) =>
        RarityRank(GetCardRarityType(cardId)) >= 4;

    private static int RarityRank(string? rarityType) =>
        rarityType switch
        {
            "rarity_birthday" => 4,
            "rarity_4" => 4,
            "rarity_3" => 3,
            "rarity_2" => 2,
            "rarity_1" => 1,
            _ => 0
        };

    private static string? GetCardRarityType(int cardId) =>
        Master.GetCardRarityType(cardId);

    private bool GrantGachaCard(int cardId)
    {
        Data.userCards ??= [];
        var existing = Data.userCards.FirstOrDefault(c => c.cardId == cardId);
        if (existing == null)
        {
            AddCard(cardId);
            return true;
        }

        existing.duplicateCount++;
        UpdateRefreshableType(nameof(SuiteUser.userCards));
        return false;
    }

    private UserResource[] GrantInitialCostumes(int cardId, long now)
    {
        var costumeIds = Master.GetCardCostume3dIds(cardId);
        if (costumeIds.Length == 0)
            return [];

        Data.userCostume3dStatuses ??= [];
        var statuses = Data.userCostume3dStatuses.ToList();
        var obtained = new List<UserResource>();
        foreach (var costumeId in costumeIds)
        {
            if (statuses.Any(s => s.costume3dId == costumeId))
                continue;

            statuses.Add(new UserCostume3DStatus
            {
                costume3dId = costumeId,
                obtainedAt = now,
                status = "available"
            });
            obtained.Add(new UserResource
            {
                resourceId = costumeId,
                resourceType = "costume_3d",
                resourceLevel = 0,
                quantity = 1
            });
        }

        if (obtained.Count == 0)
            return [];

        Data.userCostume3dStatuses = statuses.OrderBy(s => s.costume3dId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userCostume3dStatuses));
        UpdateRefreshableType(nameof(SuiteUser.userCostume3dShopItems));
        return obtained.ToArray();
    }

    private void GrantCostume3d(int costume3dId)
    {
        if (costume3dId <= 0)
            return;

        Data.userCostume3dStatuses ??= [];
        var statuses = Data.userCostume3dStatuses.ToList();
        if (statuses.Any(s => s.costume3dId == costume3dId))
            return;

        statuses.Add(new UserCostume3DStatus
        {
            costume3dId = costume3dId,
            obtainedAt = UserManager.Now,
            status = "available"
        });
        Data.userCostume3dStatuses = statuses.OrderBy(s => s.costume3dId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userCostume3dStatuses));
        UpdateRefreshableType(nameof(SuiteUser.userCostume3dShopItems));
    }

    private UserGacha UpsertUserGacha(int gachaId, int gachaBehaviorId, long now)
    {
        Data.userGachas ??= [];
        var gachas = Data.userGachas.ToList();
        var userGacha = gachas.FirstOrDefault(g =>
            g.gachaId == gachaId &&
            g.gachaBehaviorId == gachaBehaviorId);

        if (userGacha == null)
        {
            userGacha = new UserGacha
            {
                userId = GetUserId(),
                gachaId = gachaId,
                gachaBehaviorId = gachaBehaviorId
            };
            gachas.Add(userGacha);
        }

        userGacha.count++;
        userGacha.lastSpinAt = now;
        Data.userGachas = gachas.ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userGachas));
        return userGacha;
    }

    private UserResource[] GrantGachaCeilItem(MasterGacha? gacha, int gachaId, int quantity)
    {
        var ceilItemId = Master.ResolveGachaCeilItemId(gacha, gachaId);
        if (ceilItemId <= 0 || quantity <= 0)
            return [];

        Data.userGachaCeilItems ??= [];
        var ceilItems = Data.userGachaCeilItems.ToList();
        var item = ceilItems.FirstOrDefault(i => i.gachaCeilItemId == ceilItemId);
        if (item == null)
        {
            item = new UserGachaCeilItem
            {
                userId = GetUserId(),
                gachaCeilItemId = ceilItemId
            };
            ceilItems.Add(item);
        }

        item.quantity += quantity;
        Data.userGachaCeilItems = ceilItems.OrderBy(i => i.gachaCeilItemId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userGachaCeilItems));

        return
        [
            new UserResource
            {
                resourceId = ceilItemId,
                resourceType = "gacha_ceil_item",
                resourceLevel = 0,
                quantity = quantity
            }
        ];
    }

    private void GrantGachaCeilItem(int gachaCeilItemId, int quantity)
    {
        if (gachaCeilItemId <= 0 || quantity <= 0)
            return;

        Data.userGachaCeilItems ??= [];
        var ceilItems = Data.userGachaCeilItems.ToList();
        var item = ceilItems.FirstOrDefault(i => i.gachaCeilItemId == gachaCeilItemId);
        if (item == null)
        {
            item = new UserGachaCeilItem
            {
                userId = GetUserId(),
                gachaCeilItemId = gachaCeilItemId
            };
            ceilItems.Add(item);
        }

        item.quantity += quantity;
        Data.userGachaCeilItems = ceilItems.OrderBy(i => i.gachaCeilItemId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userGachaCeilItems));
    }

    private void ConsumeGachaCeilExchangeCost(MasterGachaCeilExchangeCost? cost, int exchangeCount)
    {
        if (cost == null || exchangeCount <= 0)
            return;

        var resourceId = cost.resourceId > 0 ? cost.resourceId : cost.gachaCeilItemId;
        var quantity = cost.quantity * exchangeCount;
        ConsumeResource(cost.resourceType, resourceId, quantity);
    }

    private void ConsumeGachaCeilSubstituteCost(MasterGachaCeilExchange? exchange, UserGachaCeilItemExchangeRequest request)
    {
        if (exchange?.gachaCeilExchangeSubstituteCosts == null ||
            request.gachaCeilExchangeSubstituteCostId <= 0 ||
            request.substituteCostCount <= 0)
        {
            return;
        }

        foreach (var entry in exchange.gachaCeilExchangeSubstituteCosts)
        {
            if (entry.id != request.gachaCeilExchangeSubstituteCostId)
                continue;

            var quantity = entry.substituteQuantity * request.substituteCostCount;
            ConsumeResource(entry.resourceType, entry.resourceId, quantity);
            UpsertUserGachaCeilExchangeSubstituteCost(request.gachaExchangeId, request.substituteCostCount);
            return;
        }
    }

    private void UpsertUserGachaCeilExchange(int gachaCeilExchangeId, int exchangeLimit, int exchangeCount)
    {
        Data.userGachaCeilExchanges ??= [];
        var exchanges = Data.userGachaCeilExchanges.ToList();
        var userExchange = exchanges.FirstOrDefault(e => e.gachaCeilExchangeId == gachaCeilExchangeId);
        if (userExchange == null)
        {
            userExchange = new UserGachaCeilExchange
            {
                userId = GetUserId(),
                gachaCeilExchangeId = gachaCeilExchangeId,
                exchangeStatus = "exchangeable",
                exchangeRemaining = exchangeLimit
            };
            exchanges.Add(userExchange);
        }

        if (exchangeLimit > 0)
        {
            userExchange.exchangeRemaining = Math.Max(0, userExchange.exchangeRemaining - exchangeCount);
            userExchange.exchangeStatus = userExchange.exchangeRemaining == 0 ? "not_exchangeable" : "exchangeable";
        }

        Data.userGachaCeilExchanges = exchanges.OrderBy(e => e.gachaCeilExchangeId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userGachaCeilExchanges));
    }

    private void UpsertUserGachaCeilExchangeSubstituteCost(int gachaCeilExchangeId, int usedCount)
    {
        Data.userGachaCeilExchangeSubstituteCosts ??= [];
        var costs = Data.userGachaCeilExchangeSubstituteCosts.ToList();
        var cost = costs.FirstOrDefault(c => c.gachaCeilExchangeId == gachaCeilExchangeId);
        if (cost == null)
        {
            cost = new UserGachaCeilExchangeSubstituteCost
            {
                gachaCeilExchangeId = gachaCeilExchangeId
            };
            costs.Add(cost);
        }

        cost.substituteCostUsedCount += usedCount;
        Data.userGachaCeilExchangeSubstituteCosts = costs.OrderBy(c => c.gachaCeilExchangeId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userGachaCeilExchangeSubstituteCosts));
    }

    private void ConsumeGachaCeilItem(int gachaCeilItemId, int quantity)
    {
        if (gachaCeilItemId <= 0 || quantity <= 0)
            return;

        Data.userGachaCeilItems ??= [];
        var ceilItems = Data.userGachaCeilItems.ToList();
        var item = ceilItems.FirstOrDefault(i => i.gachaCeilItemId == gachaCeilItemId);
        if (item == null)
        {
            item = new UserGachaCeilItem
            {
                userId = GetUserId(),
                gachaCeilItemId = gachaCeilItemId
            };
            ceilItems.Add(item);
        }

        item.quantity = Math.Max(0, item.quantity - quantity);
        Data.userGachaCeilItems = ceilItems.OrderBy(i => i.gachaCeilItemId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userGachaCeilItems));
    }

    public void ExchangeCards(UserCard[]? userCards)
    {
        if (userCards == null || userCards.Length == 0)
            return;

        Data.userCards ??= [];
        var cards = Data.userCards;
        foreach (var requestCard in userCards.Where(c => c.cardId > 0))
        {
            var current = cards.FirstOrDefault(c => c.cardId == requestCard.cardId);
            if (current == null)
                continue;

            var targetDuplicateCount = Math.Max(0, requestCard.duplicateCount);
            var exchangeCount = current.duplicateCount - targetDuplicateCount;
            if (exchangeCount <= 0)
                continue;

            current.duplicateCount = targetDuplicateCount;

            var exchangeResources = Master.GetCardExchangeResources(GetCardRarityType(current.cardId));
            for (var i = 0; i < exchangeCount; i++)
            {
                foreach (var resource in exchangeResources)
                    ApplyResource(resource);
            }
        }

        Data.userCards = cards.OrderBy(c => c.cardId).ToList();
        UpdateRefreshableType(nameof(SuiteUser.userCards));
    }

    public UserCardPracticeTicketResponse PracticeCardWithTickets(int cardId, UserResource[]? costs)
    {
        var card = FindOrCreateUserCard(cardId);
        var beforeTotalExp = card.totalExp;
        var beforeLevel = card.level;
        var beforeExp = Math.Max(0, beforeTotalExp - Master.GetCardLevelTotalExp(beforeLevel));

        var addExp = 0;
        if (costs != null)
        {
            foreach (var cost in costs)
            {
                if (!string.Equals(cost.resourceType, "practice_ticket", StringComparison.Ordinal) ||
                    cost.resourceId <= 0 ||
                    cost.quantity <= 0)
                    continue;

                ConsumePracticeTicket(cost.resourceId, cost.quantity);
                addExp += Master.GetPracticeTicketExp(cost.resourceId) * cost.quantity;
            }
        }

        var maxLevel = Master.GetCardMaxLevel(cardId, string.Equals(card.specialTrainingStatus, "done", StringComparison.Ordinal));
        var maxTotalExp = Master.GetCardLevelMaxTotalExp(maxLevel);
        var afterTotalExp = maxTotalExp > 0
            ? Math.Min(maxTotalExp, beforeTotalExp + addExp)
            : beforeTotalExp + addExp;
        var afterLevel = Master.GetCardLevelFromTotalExp(afterTotalExp, maxLevel);
        var afterExp = Math.Max(0, afterTotalExp - Master.GetCardLevelTotalExp(afterLevel));

        card.totalExp = afterTotalExp;
        card.level = afterLevel;
        card.exp = afterExp;
        UpdateRefreshableType(nameof(SuiteUser.userCards));
        if (afterLevel > beforeLevel)
            TouchBeginnerMissionProgress(6);

        return new UserCardPracticeTicketResponse
        {
            updateExpResult = new UpdateExpResult
            {
                beforeTotalExp = beforeTotalExp,
                afterTotalExp = afterTotalExp,
                beforeExp = beforeExp,
                afterExp = afterExp,
                beforeLevel = beforeLevel,
                afterLevel = afterLevel
            },
            updatedResources = GetRefreshData()
        };
    }

    public UserCardMasterLessonResponse MasterLessonCard(int cardId, IEnumerable<int>? masterLessonCostIds)
    {
        var card = FindOrCreateUserCard(cardId);
        var beforeMasterRank = card.masterRank;

        foreach (var cost in Master.GetMasterLessonCosts(masterLessonCostIds))
            ConsumeResource(cost.resourceType, cost.resourceId, cost.quantity);

        var requestedCount = masterLessonCostIds?.Count(id => id > 0) ?? 0;
        card.masterRank = Math.Clamp(card.masterRank + requestedCount, 0, 5);
        UpdateRefreshableType(nameof(SuiteUser.userCards));

        var obtainedRewards = new List<UserMasterLessonReward>();
        foreach (var reward in Master.GetMasterLessonRewards(cardId, beforeMasterRank, card.masterRank))
        {
            var resources = Master.BuildResourcesFromBox("master_lesson_reward", reward.resourceBoxId);
            foreach (var resource in resources)
                ApplyResource(resource);

            obtainedRewards.Add(new UserMasterLessonReward
            {
                masterLessonRewardId = reward.id,
                obtainRewards = resources
            });
        }

        return new UserCardMasterLessonResponse
        {
            obtainedRewards = obtainedRewards.ToArray(),
            updatedResources = GetRefreshData()
        };
    }

    public void SetCardSpecialTrainingStatus(int cardId, string? specialTrainingStatus)
    {
        var card = FindOrCreateUserCard(cardId);
        card.specialTrainingStatus = string.IsNullOrWhiteSpace(specialTrainingStatus)
            ? card.specialTrainingStatus
            : specialTrainingStatus;

        if (string.Equals(card.specialTrainingStatus, "done", StringComparison.Ordinal))
        {
            var maxLevel = Master.GetCardMaxLevel(cardId, true);
            card.level = Math.Min(card.level, maxLevel);
        }

        UpdateRefreshableType(nameof(SuiteUser.userCards));
    }

    public void SetCardDefaultImage(int cardId, string? defaultImage)
    {
        var card = FindOrCreateUserCard(cardId);
        if (!string.IsNullOrWhiteSpace(defaultImage))
            card.defaultImage = defaultImage;

        UpdateRefreshableType(nameof(SuiteUser.userCards));
    }

    public UserMissionReceiveResponse ReceiveBeginnerMissionV2Rewards(int[]? missionIds)
    {
        var obtainedRewards = new List<UserResource>();
        if (missionIds == null || missionIds.Length == 0)
        {
            return new UserMissionReceiveResponse
            {
                updatedResources = GetRefreshData(),
                obtainedRewards = []
            };
        }

        foreach (var missionId in missionIds.Where(id => id > 0).Distinct())
        {
            MarkMissionReceived(BeginnerMissionV2Type, missionId);
            var mission = Master.GetBeginnerMissionV2(missionId);
            foreach (var reward in mission?.rewards ?? [])
            {
                var resources = Master.BuildResourcesFromBox("mission_reward", reward.resourceBoxId);
                foreach (var resource in resources)
                {
                    ApplyResource(resource);
                    obtainedRewards.Add(resource);
                }
            }
        }

        UpdateRefreshableType(nameof(SuiteUser.userBeginnerMissionV2s));
        UpdateRefreshableType(nameof(SuiteUser.userMissionStatuses));

        return new UserMissionReceiveResponse
        {
            updatedResources = GetRefreshData(),
            obtainedRewards = obtainedRewards.ToArray()
        };
    }

    private UserCard FindOrCreateUserCard(int cardId)
    {
        Data.userCards ??= [];
        var card = Data.userCards.FirstOrDefault(c => c.cardId == cardId);
        if (card != null)
            return card;

        return AddCard(cardId);
    }

    private void TouchBeginnerMissionProgress(int missionId)
    {
        Data.userBeginnerMissionV2s ??= [];
        var missions = Data.userBeginnerMissionV2s.ToList();
        var mission = missions.FirstOrDefault(m => m.beginnerMissionV2Id == missionId);
        if (mission == null)
        {
            mission = new UserBeginnerMissionV2
            {
                beginnerMissionV2Id = missionId
            };
            missions.Add(mission);
        }

        var requirement = Master.GetBeginnerMissionV2(missionId)?.requirement ?? 1;
        mission.progress = Math.Max(mission.progress, requirement);
        mission.isNewAchieved = true;
        Data.userBeginnerMissionV2s = missions.OrderBy(m => m.beginnerMissionV2Id).ToArray();
        MarkMissionAchieved(BeginnerMissionV2Type, missionId);
        UpdateRefreshableType(nameof(SuiteUser.userBeginnerMissionV2s));
        UpdateRefreshableType(nameof(SuiteUser.userMissionStatuses));
    }

    private void MarkMissionAchieved(string missionType, int missionId)
    {
        var status = GetOrCreateMissionStatus(missionType, missionId);
        if (!string.Equals(status.missionStatus, "received", StringComparison.Ordinal))
            status.missionStatus = "achieved";
    }

    private void MarkMissionReceived(string missionType, int missionId)
    {
        var status = GetOrCreateMissionStatus(missionType, missionId);
        status.missionStatus = "received";

        if (string.Equals(missionType, BeginnerMissionV2Type, StringComparison.Ordinal))
        {
            Data.userBeginnerMissionV2s ??= [];
            var missions = Data.userBeginnerMissionV2s.ToList();
            var mission = missions.FirstOrDefault(m => m.beginnerMissionV2Id == missionId);
            if (mission == null)
            {
                mission = new UserBeginnerMissionV2
                {
                    beginnerMissionV2Id = missionId
                };
                missions.Add(mission);
            }

            var requirement = Master.GetBeginnerMissionV2(missionId)?.requirement ?? 1;
            mission.progress = Math.Max(mission.progress, requirement);
            mission.isNewAchieved = false;
            Data.userBeginnerMissionV2s = missions.OrderBy(m => m.beginnerMissionV2Id).ToArray();
        }
    }

    private UserMissionStatus GetOrCreateMissionStatus(string missionType, int missionId)
    {
        Data.userMissionStatuses ??= [];
        var statuses = Data.userMissionStatuses.ToList();
        var status = statuses.FirstOrDefault(s =>
            s.missionId == missionId &&
            string.Equals(s.missionType, missionType, StringComparison.Ordinal));

        if (status != null)
            return status;

        status = new UserMissionStatus
        {
            userId = GetUserId(),
            missionType = missionType,
            missionId = missionId,
            missionStatus = "achieved"
        };
        statuses.Add(status);
        Data.userMissionStatuses = statuses
            .OrderBy(s => s.missionType, StringComparer.Ordinal)
            .ThenBy(s => s.missionId)
            .ToArray();
        return status;
    }

    public string SetUserInherit(string password)
    {
        var inheritId = GenerateRandomString(16);

        NotSuite.InheritId = inheritId;
        NotSuite.InheritPassword = password;

        Data.userInherit = new UserInherit { inheritId = inheritId };
        UpdateRefreshableType(nameof(SuiteUser.userInherit));
        return inheritId;
    }

    public UserGamedata GetAfterUserGamedata()
    {
        var gd = Data.userGamedata!;
        return new UserGamedata
        {
            userId = gd.userId,
            name = gd.name,
            deck = gd.deck,
            rank = gd.rank
        };
    }

    public bool VerifyInherit(string inheritId, string password)
    {
        return !String.IsNullOrEmpty(inheritId) &&
               !String.IsNullOrEmpty(password) && 
               NotSuite.InheritId == inheritId &&
               NotSuite.InheritPassword == password;
    }

    public void RemoveTopic(int topicId)
    {
        if (Data.unreadUserTopics == null) return;
        Data.unreadUserTopics = Data.unreadUserTopics
            .Where(t => t.topicId != topicId).ToArray();
    }

    public void ReadEpisode(int specialEpisodeId) =>
        ReadStoryEpisode("special_story", specialEpisodeId);

    public void ReadStoryEpisode(string storyType, int episodeId, bool isNotSkipped = false)
    {
        switch (storyType)
        {
            case "unit_story":
                MarkEpisodeRead(Data.userUnitEpisodeStatuses, episodeId,
                    nameof(SuiteUser.userUnitEpisodeStatuses), isNotSkipped);
                break;
            case "special_story":
                MarkEpisodeRead(Data.userSpecialEpisodeStatuses, episodeId,
                    nameof(SuiteUser.userSpecialEpisodeStatuses), isNotSkipped);
                break;
            case "character_profile_story":
                MarkEpisodeRead(Data.userCharacterProfileEpisodeStatuses, episodeId,
                    nameof(SuiteUser.userCharacterProfileEpisodeStatuses), isNotSkipped);
                break;
            case "event_story":
                MarkEventEpisodeRead(Data.userEventEpisodeStatuses, episodeId,
                    nameof(SuiteUser.userEventEpisodeStatuses), isNotSkipped);
                break;
            case "archive_event_story":
                MarkArchiveEventEpisodeRead(Data.userArchiveEventEpisodeStatuses, episodeId,
                    nameof(SuiteUser.userArchiveEventEpisodeStatuses), isNotSkipped);
                break;
            case "card_story":
                MarkCardEpisodeRead(episodeId, isNotSkipped);
                break;
        }
    }

    public UserResource[] CompleteStoryEpisode(string storyType, int episodeId, bool isNotSkipped = false)
    {
        if (storyType == "card_story")
            return CompleteCardEpisode(episodeId, isNotSkipped);

        ReadStoryEpisode(storyType, episodeId, isNotSkipped);
        return [];
    }

    public UserResource[] ReleaseStoryEpisode(string storyType, int episodeId, string? costType)
    {
        if (storyType == "card_story")
            return ReleaseCardEpisode(episodeId, costType);

        return [];
    }

    private void MarkEpisodeRead(UserEpisodeStatus[]? statuses, int episodeId, string fieldName, bool isNotSkipped)
    {
        if (statuses == null) return;

        foreach (var status in statuses)
        {
            if (status.episodeId != episodeId) continue;

            var changed = status.status != "already_read" || status.isNotSkipped != isNotSkipped;
            status.status = "already_read";
            status.isNotSkipped = isNotSkipped;
            if (changed)
                UpdateRefreshableType(fieldName);
            return;
        }
    }

    private void MarkEventEpisodeRead(UserEventEpisodeStatus[]? statuses, int episodeId, string fieldName, bool isNotSkipped)
    {
        if (statuses == null) return;

        foreach (var status in statuses)
        {
            if (status.episodeId != episodeId) continue;

            var changed = status.status != "already_read" || status.isNotSkipped != isNotSkipped;
            status.status = "already_read";
            status.isNotSkipped = isNotSkipped;
            if (changed)
                UpdateRefreshableType(fieldName);
            return;
        }
    }

    private void MarkArchiveEventEpisodeRead(UserArchiveEventEpisodeStatus[]? statuses, int episodeId, string fieldName, bool isNotSkipped)
    {
        if (statuses == null) return;

        foreach (var status in statuses)
        {
            if (status.episodeId != episodeId) continue;

            var changed = status.status != "already_read" || status.isNotSkipped != isNotSkipped;
            status.status = "already_read";
            status.isNotSkipped = isNotSkipped;
            if (changed)
                UpdateRefreshableType(fieldName);
            return;
        }
    }

    private UserCardEpisode? FindCardEpisode(int cardEpisodeId)
    {
        if (Data.userCards == null) return null;

        foreach (var card in Data.userCards)
        {
            if (card.episodes == null) continue;

            foreach (var episode in card.episodes)
            {
                if (episode.cardEpisodeId == cardEpisodeId)
                    return episode;
            }
        }

        return null;
    }

    private void MarkCardEpisodeRead(int cardEpisodeId, bool isNotSkipped)
    {
        var episode = FindCardEpisode(cardEpisodeId);
        if (episode == null) return;

        var changed = episode.scenarioStatus != "already_read" || episode.isNotSkipped != isNotSkipped;
        episode.scenarioStatus = "already_read";
        episode.scenarioStatusReasons = [];
        episode.isNotSkipped = isNotSkipped;
        if (changed)
            UpdateRefreshableType(nameof(SuiteUser.userCards));
    }

    private UserResource[] CompleteCardEpisode(int cardEpisodeId, bool isNotSkipped)
    {
        var episode = FindCardEpisode(cardEpisodeId);
        var wasAlreadyRead = string.Equals(episode?.scenarioStatus, "already_read", StringComparison.Ordinal);

        MarkCardEpisodeRead(cardEpisodeId, isNotSkipped);

        if (episode == null || wasAlreadyRead)
            return [];

        var rewards = Master.BuildResourcesFromBoxes(
            "episode_reward",
            Master.GetMasterCardEpisode(cardEpisodeId)?.rewardResourceBoxIds);

        ApplyLiveRewards(rewards);

        UpdateRefreshableTypes(new[]
        {
            nameof(SuiteUser.userCharacterMissionV2s),
            nameof(SuiteUser.userCharacterMissionV2Statuses)
        });

        return rewards;
    }

    private UserResource[] ReleaseCardEpisode(int cardEpisodeId, string? costType)
    {
        var episode = FindCardEpisode(cardEpisodeId);
        if (episode == null)
            return [];

        var wasUnlocked =
            string.Equals(episode.scenarioStatus, "released", StringComparison.Ordinal) ||
            string.Equals(episode.scenarioStatus, "already_read", StringComparison.Ordinal);

        var consumed = wasUnlocked ? [] : BuildCardEpisodeReleaseCosts(cardEpisodeId, costType);
        foreach (var resource in consumed)
        {
            ConsumeResource(resource.resourceType, resource.resourceId, resource.quantity);
        }

        if (episode != null)
        {
            episode.scenarioStatus = "released";
            episode.scenarioStatusReasons = [];
            UpdateRefreshableType(nameof(SuiteUser.userCards));
        }

        return consumed;
    }

    private UserResource[] BuildCardEpisodeReleaseCosts(int cardEpisodeId, string? costType)
    {
        var normalizedCostType = string.IsNullOrEmpty(costType)
            ? CardEpisodeReleaseCostTypeCommonMaterial
            : costType;

        if (string.Equals(normalizedCostType, CardEpisodeReleaseCostTypeTicket, StringComparison.Ordinal))
        {
            var materialId = Master.GetConfigInt(CardEpisodeReleaseTicketMaterialIdConfig);
            var quantity = Master.GetConfigInt(CardEpisodeReleaseCostQuantityConfig, 1);
            return materialId <= 0 || quantity <= 0
                ? []
                :
                [
                    new UserResource
                    {
                        resourceType = "material",
                        resourceId = materialId,
                        quantity = quantity
                    }
                ];
        }

        var costs = Master.GetMasterCardEpisode(cardEpisodeId)?.costs;
        if (costs == null)
            return [];

        return costs
            .Where(cost => cost.quantity > 0)
            .Select(cost => new UserResource
            {
                resourceType = cost.resourceType,
                resourceId = cost.resourceId,
                quantity = cost.quantity
            })
            .ToArray();
    }

    public List<UserPresentData> ReceivePresent(string[] presentIds)
    {
        var received = new List<UserPresentData>();
        foreach (var pid in presentIds)
        {
            var result = ReceiveOnePresent(pid);
            if (result != null)
                received.Add(result);
        }
        return received;
    }

    private UserPresentData? ReceiveOnePresent(string presentId)
    {
        if (Data.userPresents == null) return null;

        var idx = Data.userPresents.FindIndex(p => p.presentId == presentId);
        if (idx < 0) return null;

        var present = Data.userPresents[idx];
        Data.userPresents.RemoveAt(idx);

        ApplyResource(BuildResourceFromPresent(present));
        UpdateRefreshableType(nameof(SuiteUser.userPresents));

        NotSuite.PresentHistories.Add(new UserPresentHistoryData
        {
            presentId = present.presentId,
            seq = present.seq,
            resourceType = present.resourceType,
            resourceId = present.resourceId,
            resourceLevel = present.resourceLevel,
            resourceQuantity = present.resourceQuantity,
            expiredAt = present.expiredAt,
            receivedAt = UserManager.Now,
            reason = present.reason
        });

        return present;
    }

    private static UserResource BuildResourceFromPresent(UserPresentData present) =>
        new()
        {
            resourceType = present.resourceType,
            resourceId = present.resourceId,
            resourceLevel = present.resourceLevel,
            quantity = present.resourceQuantity
        };

    public List<UserPresentHistoryData> GetPresentHistory()
    {
        return NotSuite.PresentHistories;
    }

    public void UpdateProfile(UserProfile newProfile)
    {
        if (Data.userProfile == null) return;
        // 保留原 userId
        newProfile.userId = Data.userProfile.userId;
        Data.userProfile = newProfile;
        UpdateRefreshableType(nameof(SuiteUser.userProfile));
    }

    public void MergeUserGamedata(UserGamedata patch)
    {
        if (Data.userGamedata == null)
        {
            Data.userGamedata = patch;
            UpdateRefreshableType(nameof(SuiteUser.userGamedata));
            return;
        }

        var current = Data.userGamedata;
        if (patch.userId != 0) current.userId = patch.userId;
        if (patch.name != null) current.name = patch.name;
        if (patch.deck != 0) current.deck = patch.deck;
        if (patch.rank != 0) current.rank = patch.rank;
        if (patch.exp != 0) current.exp = patch.exp;
        if (patch.totalExp != 0) current.totalExp = patch.totalExp;
        if (patch.coin != 0) current.coin = patch.coin;
        if (patch.virtualCoin != 0) current.virtualCoin = patch.virtualCoin;
        if (patch.lastLoginAt != 0) current.lastLoginAt = patch.lastLoginAt;
        current.customProfileId = patch.customProfileId;

        UpdateRefreshableType(nameof(SuiteUser.userGamedata));
    }

    public void UpdateUserGamedata(UserGamedata newGamedata) =>
        MergeUserGamedata(newGamedata);
    

    public void SetCurrentCustomProfile(int? customProfileId)
    {
        Data.userGamedata ??= new UserGamedata { userId = GetUserId() };
        Data.userGamedata.customProfileId = customProfileId;
        UpdateRefreshableType(nameof(SuiteUser.userGamedata));
    }

    public void SaveCustomProfile(
        int customProfileId,
        string? name,
        List<UserCustomProfileCardOrder>? customProfileCardOrders)
    {
        var profiles = Data.userCustomProfiles!.ToList();
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

        var cards = Data.userCustomProfileCards!.ToList();
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

        Data.userCustomProfiles = profiles.ToArray();
        Data.userCustomProfileCards = cards.ToArray();
        UpdateCustomProfileResourceUsages(customProfileId);

        UpdateRefreshableType(nameof(SuiteUser.userCustomProfiles));
        UpdateRefreshableType(nameof(SuiteUser.userCustomProfileCards));
    }

    public void SaveCustomProfileCard(
        int customProfileId,
        int customProfileCardId,
        UserSaveCustomProfileCardRequest request)
    {
        EnsureCustomProfileExists(customProfileId);

        var cards = Data.userCustomProfileCards!.ToList();
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

            var thumbnailPath = CustomProfileThumbnailStore.SaveThumbnail(request.thumbnail);
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
            card.thumbnailPath = CustomProfileThumbnailStore.SaveThumbnail(request.thumbnail, card.thumbnailPath);
            card.customProfileCard = request.customProfileCard;
        }

        Data.userCustomProfileCards = cards.ToArray();
        UpdateCustomProfileResourceUsages(customProfileId);
        UpdateRefreshableType(nameof(SuiteUser.userCustomProfileCards));
    }

    public void DeleteCustomProfileCards(int customProfileId, int[] customProfileCardIds)
    {
        var deleteIds = customProfileCardIds.ToHashSet();
        var cards = Data.userCustomProfileCards!
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

        Data.userCustomProfileCards = cards.ToArray();
        UpdateCustomProfileResourceUsages(customProfileId);
        UpdateRefreshableType(nameof(SuiteUser.userCustomProfileCards));
    }

    public void UpdateCustomProfileResourceUsages(int customProfileId)
    {
        var usageCounts = new Dictionary<int, int>();
        foreach (var card in Data.userCustomProfileCards!.Where(c => c.customProfileId == customProfileId))
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

        var usages = Data.userCustomProfileResourceUsages!
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

        Data.userCustomProfileResourceUsages = usages.ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userCustomProfileResourceUsages));
    }

    private void EnsureCustomProfileExists(int customProfileId)
    {
        var profiles = Data.userCustomProfiles!.ToList();
        if (profiles.Any(p => p.customProfileId == customProfileId))
            return;

        profiles.Add(new UserCustomProfile
        {
            customProfileId = customProfileId,
            name = ""
        });
        Data.userCustomProfiles = profiles.ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userCustomProfiles));
    }

    public void PurchaseShopItem(int shopId, int shopItemId)
    {
        var shopItem = Master.GetMasterShopItem(shopId, shopItemId);
        var wasSoldOut = IsShopItemSoldOut(shopId, shopItemId);

        MarkShopItemSoldOut(shopId, shopItemId);

        if (!wasSoldOut && shopItem?.costs != null)
            ConsumeShopItemCosts(shopItem.costs);

        var resourceBoxId = shopItem?.resourceBoxId ?? shopItemId;
        ApplyShopItemResources(resourceBoxId);
    }

    private bool IsShopItemSoldOut(int shopId, int shopItemId) =>
        Data.userShops?
            .FirstOrDefault(s => s.shopId == shopId)?
            .userShopItems?
            .Any(i => i.shopItemId == shopItemId &&
                      string.Equals(i.status, UserShopItem.STATUS_SOLD_OUT, StringComparison.Ordinal)) == true;

    private void MarkShopItemSoldOut(int shopId, int shopItemId)
    {
        Data.userShops ??= [];
        var shops = Data.userShops.ToList();
        var shop = shops.FirstOrDefault(s => s.shopId == shopId);
        if (shop == null)
        {
            shop = new UserShop
            {
                shopId = shopId,
                userShopItems = []
            };
            shops.Add(shop);
            Data.userShops = shops.ToArray();
        }

        var items = (shop.userShopItems ?? []).ToList();
        var item = items.FirstOrDefault(i => i.shopItemId == shopItemId);
        if (item == null)
        {
            item = new UserShopItem
            {
                shopItemId = shopItemId
            };
            items.Add(item);
        }

        item.status = UserShopItem.STATUS_SOLD_OUT;
        shop.userShopItems = items.ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userShops));
    }

    private void ConsumeShopItemCosts(MasterShopItemCostEntry[] costs)
    {
        foreach (var entry in costs)
        {
            var cost = entry.cost;
            if (cost == null)
                continue;

            var resourceType = cost.resourceType;
            var resourceId = cost.resourceId;
            var quantity = cost.quantity;
            if (quantity <= 0)
                continue;

            switch (resourceType)
            {
                case "material":
                    ConsumeMaterial(resourceId, quantity);
                    break;
                case "coin":
                    if (Data.userGamedata != null)
                    {
                        Data.userGamedata.coin = Math.Max(0, Data.userGamedata.coin - quantity);
                        UpdateRefreshableType(nameof(SuiteUser.userGamedata));
                    }
                    break;
                case "jewel":
                    ConsumeJewel(quantity);
                    break;
            }
        }
    }

    private void ConsumeMaterial(int materialId, int quantity)
    {
        if (materialId <= 0 || quantity <= 0)
            return;

        Data.userMaterials ??= [];
        var materials = Data.userMaterials.ToList();
        var material = materials.FirstOrDefault(m => m.materialId == materialId);
        if (material == null)
        {
            material = new UserMaterial
            {
                materialId = materialId
            };
            materials.Add(material);
        }

        material.quantity = Math.Max(0, material.quantity - quantity);
        Data.userMaterials = materials.OrderBy(m => m.materialId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userMaterials));
    }

    private void ConsumeJewel(int quantity)
    {
        if (quantity <= 0)
            return;

        Data.userChargedCurrency ??= new ChargedCurrency { paidUnitPrices = [] };

        var freeCost = Math.Min(Data.userChargedCurrency.free, quantity);
        Data.userChargedCurrency.free -= freeCost;

        var remainingCost = quantity - freeCost;
        if (remainingCost > 0)
            Data.userChargedCurrency.paid = Math.Max(0, Data.userChargedCurrency.paid - remainingCost);

        UpdateRefreshableType(nameof(SuiteUser.userChargedCurrency));
    }

    private void ConsumeResource(string? resourceType, int resourceId, int quantity)
    {
        if (quantity <= 0)
            return;

        switch (resourceType)
        {
            case "material":
                ConsumeMaterial(resourceId, quantity);
                break;
            case "coin":
                if (Data.userGamedata != null)
                {
                    Data.userGamedata.coin = Math.Max(0, Data.userGamedata.coin - quantity);
                    UpdateRefreshableType(nameof(SuiteUser.userGamedata));
                }
                break;
            case "jewel":
                ConsumeJewel(quantity);
                break;
            case "paid_jewel":
                ConsumePaidJewelForGacha(quantity);
                break;
            case "gacha_ceil_item":
                ConsumeGachaCeilItem(resourceId, quantity);
                break;
            case "gacha_ticket":
                ConsumeGachaTicketForGacha(resourceId, quantity);
                break;
            case "practice_ticket":
                ConsumePracticeTicket(resourceId, quantity);
                break;
        }
    }

    private void ApplyResource(UserResource resource)
    {
        if (resource.quantity <= 0)
            return;

        switch (resource.resourceType)
        {
            case "card":
                for (var i = 0; i < resource.quantity; i++)
                    GrantGachaCard(resource.resourceId);
                break;
            case "costume_3d":
                GrantCostume3d(resource.resourceId);
                break;
            case "music":
                GrantMusic(resource.resourceId);
                GrantDefaultMusicVocals(resource.resourceId);
                break;
            case "music_vocal":
                GrantMusicVocal(resource.resourceId);
                break;
            case "gacha_ceil_item":
                GrantGachaCeilItem(resource.resourceId, resource.quantity);
                break;
            case "gacha_ticket":
                AddGachaTicket(resource.resourceId, resource.quantity);
                break;
            case "boost_item":
                AddBoostItem(resource.resourceId, resource.quantity);
                break;
            case "avatar_motion":
                AddAvatarMotion(resource.resourceId);
                break;
            case "mysekai_item":
                AddMysekaiItem(resource.resourceId, resource.quantity);
                break;
            case "mysekai_tool":
                AddMysekaiTool(resource.resourceId, resource.quantity);
                break;
            default:
                ApplyLiveRewards([resource]);
                break;
        }
    }

    private static void AddObtainedResource(
        IDictionary<(string? Type, int Id, int Level), UserResource> obtained,
        UserResource resource)
    {
        var key = (resource.resourceType, resource.resourceId, resource.resourceLevel);
        if (obtained.TryGetValue(key, out var current))
        {
            current.quantity += resource.quantity;
            return;
        }

        obtained[key] = new UserResource
        {
            resourceType = resource.resourceType,
            resourceId = resource.resourceId,
            resourceLevel = resource.resourceLevel,
            quantity = resource.quantity
        };
    }

    private void ApplyShopItemResources(int resourceBoxId)
    {
        foreach (var resource in Master.BuildResourcesFromBox("shop_item", resourceBoxId))
        {
            if (resource.quantity <= 0)
                continue;

            switch (resource.resourceType)
            {
                case "music":
                    GrantMusic(resource.resourceId);
                    GrantDefaultMusicVocals(resource.resourceId);
                    break;
                case "music_vocal":
                    GrantMusicVocal(resource.resourceId);
                    break;
                default:
                    ApplyLiveRewards([resource]);
                    break;
            }
        }
    }

    private void GrantMusic(int musicId)
    {
        if (musicId <= 0)
            return;

        Data.userMusics ??= [];
        var musics = Data.userMusics.ToList();
        if (musics.Any(m => m.musicId == musicId))
            return;

        musics.Add(new UserMusic { musicId = musicId });
        Data.userMusics = musics.OrderBy(m => m.musicId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userMusics));
    }

    private void GrantDefaultMusicVocals(int musicId)
    {
        foreach (var vocal in Master.GetMasterMusicVocals())
        {
            if (vocal.musicId != musicId || vocal.releaseConditionId != 5)
                continue;

            GrantMusicVocal(musicId, vocal.id);
        }
    }

    private void GrantMusicVocal(int musicVocalId)
    {
        if (musicVocalId <= 0)
            return;

        var vocal = Master.GetMasterMusicVocal(musicVocalId);
        if (vocal != null)
            GrantMusicVocal(vocal.musicId, musicVocalId);
    }

    private void GrantMusicVocal(int musicId, int musicVocalId)
    {
        if (musicId <= 0 || musicVocalId <= 0)
            return;

        Data.userMusicVocals ??= [];
        if (Data.userMusicVocals.Any(v => v.musicVocalId == musicVocalId))
            return;

        Data.userMusicVocals.Add(new UserMusicVocal
        {
            musicId = musicId,
            musicVocalId = musicVocalId
        });
        Data.userMusicVocals = Data.userMusicVocals
            .OrderBy(v => v.musicVocalId)
            .ToList();
        UpdateRefreshableType(nameof(SuiteUser.userMusicVocals));
    }

    // ===================== Live Mixin =====================

    public UserLive StartUserLive(UserLiveRequest request)
    {
        var userLiveId = Guid.NewGuid().ToString();
        NotSuite.UserLiveSessions[userLiveId] = new UserLiveSessionData
        {
            UserLiveId = userLiveId,
            MusicId = request.musicId,
            MusicDifficultyId = request.musicDifficultyId,
            MusicVocalId = request.musicVocalId,
            DeckId = request.deckId,
            BoostCount = request.boostCount,
            IsAuto = request.isAuto,
            MusicCategoryName = request.musicCategoryName,
            CustomMusicScoreId = request.customMusicScoreId,
            CreatedAt = UserManager.Now
        };

        UpdateRefreshableType(nameof(SuiteUser.userEventBreakTime));
        return new UserLive
        {
            userLiveId = userLiveId,
            updatedResources = GetRefreshData(),
            skills = BuildIngameLotterySkills(request.deckId),
            comboCutins = [],
            isInBreakTime = false
        };
    }

    public UserLiveClearResponse ClearUserLive(string userLiveId, UserLiveClearRequest request)
    {
        NotSuite.UserLiveSessions.Remove(userLiveId, out var session);

        var fullCombo = request.badCount == 0 && request.missCount == 0;
        var fullPerfect = request.greatCount == 0 &&
                          request.goodCount == 0 &&
                          request.badCount == 0 &&
                          request.missCount == 0;

        var scoreRank = session != null
            ? Master.BuildScoreRank(session.MusicDifficultyId, request.score)
            : MasterDataManager.BuildScoreRank(request.score);
        var boost = Master.BuildMasterBoost(session?.BoostCount ?? 0);
        var userLivePoint = BuildUserLivePoint(boost);
        var highScoreFlg = false;
        DeckCardUpdateExpResult[] deckCardExpResults = [];
        UserMusicAchievement[] grantedMusicAchievements = [];
        UserResource[] scoreRankRewards = [];
        UserResource[] musicAchievementRewards = [];
        if (session != null)
        {
            highScoreFlg = UpdateUserMusicResult(session, request, fullCombo, fullPerfect);
            deckCardExpResults = BuildDeckCardExpResults(session.DeckId);
            grantedMusicAchievements = GrantUserMusicAchievements(session, request, scoreRank);
            scoreRankRewards = BuildScoreRankRewards(session.MusicDifficultyId, scoreRank, boost);
            musicAchievementRewards = BuildMusicAchievementRewards(grantedMusicAchievements);
            ApplyLiveRewards(scoreRankRewards);
            ApplyLiveRewards(musicAchievementRewards);
            UpdateLiveMissionProgress(userLivePoint);
            ConsumeBoost(session.BoostCount);
        }

        MergeLiveCharacterArchiveVoiceGroups(request.ingameCutinCharacterArchiveVoiceGroupIds);

        return new UserLiveClearResponse
        {
            updatedResources = GetRefreshData(),
            scoreRank = scoreRank,
            score = request.score,
            perfectCount = request.perfectCount,
            greatCount = request.greatCount,
            goodCount = request.goodCount,
            badCount = request.badCount,
            missCount = request.missCount,
            maxCombo = request.maxCombo,
            highScoreFlg = highScoreFlg,
            fullComboFlg = fullCombo,
            fullPerfectFlg = fullPerfect,
            userExpResult = BuildNoopExpResult(),
            deckCardExpResults = deckCardExpResults,
            unitExpResults = [],
            userDeck = GetUserDeck(session?.DeckId),
            userMusicAchievements = grantedMusicAchievements,
            scoreRankRewards = scoreRankRewards,
            playerRankRewards = [],
            limitedTermScoreRankRewards = [],
            musicAchievementRewards = musicAchievementRewards,
            boost = boost,
            beforeEventPoint = 0,
            afterEventPoint = 0,
            beforeEventItemQuantity = 0,
            afterEventItemQuantity = 0,
            beforeWorldBloomChapterPoint = null,
            afterWorldBloomChapterPoint = null,
            worldBloomChapterNo = null,
            isPreliminaryTournament = false,
            bondsUpdateExpResults = [],
            userEventDeviceTransferRestrict = new UserRestrictInfo(),
            userLivePoint = userLivePoint,
            isEventMaintenance = false,
            isInBreakTime = false
        };
    }

    public void ReceiveLiveCharacterArchiveVoiceResult(int liveResultCharacterArchiveVoiceGroupId)
    {
        if (liveResultCharacterArchiveVoiceGroupId <= 0)
            return;

        MergeLiveCharacterArchiveVoiceGroups([liveResultCharacterArchiveVoiceGroupId]);
    }

    private bool UpdateUserMusicResult(
        UserLiveSessionData session,
        UserLiveClearRequest request,
        bool fullCombo,
        bool fullPerfect)
    {
        Data.userMusicResults ??= [];
        var results = Data.userMusicResults.ToList();
        var difficultyType = Master.ResolveMusicDifficultyType(session.MusicDifficultyId);
        var result = results.FirstOrDefault(r =>
            r.musicId == session.MusicId &&
            string.Equals(r.musicDifficultyType, difficultyType, StringComparison.Ordinal));

        var previousHighScore = result?.highScore ?? 0;
        var highScoreFlg = request.score > previousHighScore;

        if (result == null)
        {
            result = new UserMusicResult
            {
                musicId = session.MusicId,
                musicDifficultyType = difficultyType,
                playType = "solo",
                playResult = "clear"
            };
            results.Add(result);
        }

        result.playType = "solo";
        result.playResult = BuildPlayResult(fullCombo, fullPerfect, request.life);
        result.highScore = Math.Max(result.highScore, request.score);
        result.fullComboFlg = result.fullComboFlg || fullCombo;
        result.fullPerfectFlg = result.fullPerfectFlg || fullPerfect;

        Data.userMusicResults = results.ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userMusicResults));
        return highScoreFlg;
    }

    private UserMusicAchievement[] GrantUserMusicAchievements(
        UserLiveSessionData session,
        UserLiveClearRequest request,
        string scoreRank)
    {
        var achievementIds = Master.ResolveMusicAchievementIds(session.MusicDifficultyId, request.maxCombo, scoreRank);

        Data.userMusicAchievements ??= [];
        var achievements = Data.userMusicAchievements.ToList();
        var granted = new List<UserMusicAchievement>();

        foreach (var achievementId in achievementIds.Distinct())
        {
            if (achievements.Any(a => a.musicId == session.MusicId && a.musicAchievementId == achievementId))
                continue;

            var achievement = new UserMusicAchievement
            {
                musicId = session.MusicId,
                musicAchievementId = achievementId
            };
            achievements.Add(achievement);
            granted.Add(achievement);
        }

        if (granted.Count > 0)
        {
            Data.userMusicAchievements = achievements.ToArray();
            UpdateRefreshableType(nameof(SuiteUser.userMusicAchievements));
        }

        return granted.ToArray();
    }

    private static UserResource[] BuildMusicAchievementRewards(UserMusicAchievement[] achievements)
    {
        var rewards = new List<UserResource>();
        foreach (var achievement in achievements)
        {
            var resourceBoxId = Master.GetMusicAchievementResourceBoxId(achievement.musicAchievementId);
            rewards.AddRange(Master.BuildResourcesFromBox("music_achievement", resourceBoxId));
        }

        return rewards.ToArray();
    }

    private static UserResource[] BuildScoreRankRewards(int musicDifficultyId, string scoreRank, MasterBoost boost)
    {
        var playLevel = Master.ResolveMusicPlayLevel(musicDifficultyId);
        var resourceBoxIds = GetScoreRankRewardResourceBoxIds(playLevel, scoreRank);
        if (resourceBoxIds.Length == 0)
            return [];

        var rewardRate = Math.Max(1, boost.rewardRate);
        return resourceBoxIds
            .SelectMany(resourceBoxId => Master.BuildResourcesFromBox("score_rank_reward_detail", resourceBoxId))
            .Select(reward => new UserResource
            {
                resourceType = reward.resourceType,
                resourceId = reward.resourceId,
                resourceLevel = reward.resourceLevel,
                quantity = reward.quantity * rewardRate
            })
            .Where(reward => reward.quantity > 0)
            .ToArray();
    }

    private static int[] GetScoreRankRewardResourceBoxIds(int playLevel, string scoreRank) =>
        (playLevel, scoreRank) switch
        {
            // 6.5.5 capture 0054: musicDifficultyId 406, playLevel 6, rank_c.
            (6, "rank_c") => [62, 12, 19, 15, 47, 56],

            // 6.5.5 capture 0061: musicDifficultyId 313, playLevel 17, rank_d.
            (17, "rank_d") => [61, 20],

            _ => []
        };

    private void ApplyLiveRewards(IEnumerable<UserResource> rewards)
    {
        foreach (var reward in rewards)
        {
            if (reward.quantity <= 0)
                continue;

            switch (reward.resourceType)
            {
                case "jewel":
                    Data.userChargedCurrency ??= new ChargedCurrency { paidUnitPrices = [] };
                    Data.userChargedCurrency.free += reward.quantity;
                    UpdateRefreshableType(nameof(SuiteUser.userChargedCurrency));
                    break;
                case "coin":
                    if (Data.userGamedata != null)
                    {
                        Data.userGamedata.coin += reward.quantity;
                        UpdateRefreshableType(nameof(SuiteUser.userGamedata));
                    }
                    break;
                case "virtual_coin":
                    if (Data.userGamedata != null)
                    {
                        Data.userGamedata.virtualCoin += reward.quantity;
                        UpdateRefreshableType(nameof(SuiteUser.userGamedata));
                    }
                    break;
                case "material":
                    AddMaterial(reward.resourceId, reward.quantity);
                    break;
                case "practice_ticket":
                    AddPracticeTicket(reward.resourceId, reward.quantity);
                    break;
                case "costume_3d":
                    GrantCostume3d(reward.resourceId);
                    break;
            }
        }
    }

    private void ConsumePracticeTicket(int practiceTicketId, int quantity)
    {
        if (practiceTicketId <= 0 || quantity <= 0)
            return;

        Data.userPracticeTickets ??= [];
        var tickets = Data.userPracticeTickets.ToList();
        var ticket = tickets.FirstOrDefault(t => t.practiceTicketId == practiceTicketId);
        if (ticket == null)
        {
            ticket = new UserPracticeTicket
            {
                userId = GetUserId(),
                practiceTicketId = practiceTicketId
            };
            tickets.Add(ticket);
        }

        ticket.quantity = Math.Max(0, ticket.quantity - quantity);
        Data.userPracticeTickets = tickets.OrderBy(t => t.practiceTicketId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userPracticeTickets));
    }

    private void AddMaterial(int materialId, int quantity)
    {
        if (materialId <= 0 || quantity <= 0)
            return;

        Data.userMaterials ??= [];
        var materials = Data.userMaterials.ToList();
        var material = materials.FirstOrDefault(m => m.materialId == materialId);
        if (material == null)
        {
            material = new UserMaterial { materialId = materialId };
            materials.Add(material);
        }

        material.quantity += quantity;
        Data.userMaterials = materials.ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userMaterials));
    }

    private void AddPracticeTicket(int practiceTicketId, int quantity)
    {
        if (practiceTicketId <= 0 || quantity <= 0)
            return;

        Data.userPracticeTickets ??= [];
        var tickets = Data.userPracticeTickets.ToList();
        var ticket = tickets.FirstOrDefault(t => t.practiceTicketId == practiceTicketId);
        if (ticket == null)
        {
            ticket = new UserPracticeTicket
            {
                userId = GetUserId(),
                practiceTicketId = practiceTicketId
            };
            tickets.Add(ticket);
        }

        ticket.quantity += quantity;
        Data.userPracticeTickets = tickets.ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userPracticeTickets));
    }

    private void AddGachaTicket(int gachaTicketId, int quantity)
    {
        if (gachaTicketId <= 0 || quantity <= 0)
            return;

        Data.userGachaTickets ??= [];
        var tickets = Data.userGachaTickets.ToList();
        var ticket = tickets.FirstOrDefault(t => t.gachaTicketId == gachaTicketId);
        if (ticket == null)
        {
            ticket = new UserGachaTicket
            {
                userId = GetUserId(),
                gachaTicketId = gachaTicketId
            };
            tickets.Add(ticket);
        }

        ticket.quantity += quantity;
        Data.userGachaTickets = tickets.OrderBy(t => t.gachaTicketId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userGachaTickets));
    }

    private void AddBoostItem(int boostItemId, int quantity)
    {
        if (boostItemId <= 0 || quantity <= 0)
            return;

        Data.userBoostItems ??= [];
        var items = Data.userBoostItems.ToList();
        var item = items.FirstOrDefault(i => i.boostItemId == boostItemId);
        if (item == null)
        {
            item = new UserBoostItem
            {
                userId = GetUserId(),
                boostItemId = boostItemId
            };
            items.Add(item);
        }

        item.quantity += quantity;
        Data.userBoostItems = items.OrderBy(i => i.boostItemId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userBoostItems));
    }

    private void AddAvatarMotion(int avatarMotionId)
    {
        if (avatarMotionId <= 0)
            return;

        Data.userAvatarMotions ??= [];
        var motions = Data.userAvatarMotions.ToList();
        if (motions.Any(m => m.avatarMotionId == avatarMotionId))
            return;

        motions.Add(new UserAvatarMotion { avatarMotionId = avatarMotionId });
        Data.userAvatarMotions = motions.OrderBy(m => m.avatarMotionId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userAvatarMotions));
    }

    private void AddMysekaiItem(int mysekaiItemId, int quantity)
    {
        if (mysekaiItemId <= 0 || quantity <= 0)
            return;

        Data.userMysekaiItems ??= [];
        var items = Data.userMysekaiItems.ToList();
        var item = items.FirstOrDefault(i => i.mysekaiItemId == mysekaiItemId);
        if (item == null)
        {
            item = new UserMysekaiItem
            {
                mysekaiItemId = mysekaiItemId,
                lastObtainedAt = UserManager.Now
            };
            items.Add(item);
        }

        item.quantity += quantity;
        item.lastObtainedAt = UserManager.Now;
        Data.userMysekaiItems = items.OrderBy(i => i.mysekaiItemId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userMysekaiItems));
    }

    private void AddMysekaiTool(int mysekaiToolId, int quantity)
    {
        if (mysekaiToolId <= 0 || quantity <= 0)
            return;

        Data.userMysekaiTools ??= [];
        var tools = Data.userMysekaiTools.ToList();
        var tool = tools.FirstOrDefault(t => t.mysekaiToolId == mysekaiToolId);
        if (tool == null)
        {
            tool = new UserMysekaiTool
            {
                mysekaiToolId = mysekaiToolId,
                durability = Master.GetMysekaiToolMaxDurability(mysekaiToolId),
                lastObtainedAt = UserManager.Now
            };
            tools.Add(tool);
        }

        tool.quantity += quantity;
        tool.lastObtainedAt = UserManager.Now;
        Data.userMysekaiTools = tools.OrderBy(t => t.mysekaiToolId).ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userMysekaiTools));
    }

    private void ConsumeBoost(int boostCount)
    {
        if (Data.userBoost == null || boostCount <= 0)
            return;

        Data.userBoost.current = Math.Max(0, Data.userBoost.current - boostCount);
        Data.userBoost.recoveryAt = (ulong)UserManager.Now;
        UpdateRefreshableType(nameof(SuiteUser.userBoost));
    }

    private void MergeLiveCharacterArchiveVoiceGroups(IEnumerable<int>? groupIds)
    {
        if (groupIds == null)
            return;

        Data.userLiveCharacterArchiveVoice ??= new UserLiveCharacterArchiveVoice
        {
            characterArchiveVoiceGroupIds = []
        };
        Data.userLiveCharacterArchiveVoice.characterArchiveVoiceGroupIds ??= [];

        var changed = false;
        var current = Data.userLiveCharacterArchiveVoice.characterArchiveVoiceGroupIds;
        foreach (var groupId in groupIds.Where(id => id > 0))
        {
            if (current.Contains(groupId))
                continue;

            current.Add(groupId);
            changed = true;
        }

        if (changed)
            UpdateRefreshableType(nameof(SuiteUser.userLiveCharacterArchiveVoice));
    }

    private UserDeck? GetUserDeck(int? deckId)
    {
        if (deckId == null || Data.userDecks == null)
            return null;

        return Data.userDecks.FirstOrDefault(d => d.deckId == deckId.Value);
    }

    private IngameLotterySkill[] BuildIngameLotterySkills(int deckId)
    {
        var deck = GetUserDeck(deckId);
        if (deck == null)
            return [];

        var cardIds = new[] { deck.leader, deck.member1, deck.member2, deck.member3, deck.member4, deck.member5 };
        return cardIds
            .Where(cardId => cardId > 0)
            .Select((cardId, index) => new IngameLotterySkill
            {
                seq = index + 1,
                cardId = cardId,
                relationCardId = null,
                ingameCutinCharacterId = null
            })
            .ToArray();
    }

    private DeckCardUpdateExpResult[] BuildDeckCardExpResults(int deckId)
    {
        var deck = GetUserDeck(deckId);
        if (deck == null || Data.userCards == null)
            return [];

        var cardIds = new[] { deck.member1, deck.member2, deck.member3, deck.member4, deck.member5 };
        return cardIds
            .Select((cardId, index) => new { cardId, index })
            .Where(slot => slot.cardId > 0 && Data.userCards.Any(card => card.cardId == slot.cardId))
            .Select(slot =>
            {
                var card = Data.userCards!.First(card => card.cardId == slot.cardId);
                return new DeckCardUpdateExpResult
                {
                    index = slot.index + 1,
                    expResult = new UpdateExpResult
                    {
                        beforeTotalExp = card.totalExp,
                        afterTotalExp = card.totalExp,
                        beforeExp = card.exp,
                        afterExp = card.exp,
                        beforeLevel = card.level,
                        afterLevel = card.level
                    }
                };
            })
            .ToArray();
    }

    private UpdateExpResult BuildNoopExpResult()
    {
        var gd = Data.userGamedata;
        return new UpdateExpResult
        {
            beforeTotalExp = gd?.totalExp ?? 0,
            afterTotalExp = gd?.totalExp ?? 0,
            beforeExp = gd?.exp ?? 0,
            afterExp = gd?.exp ?? 0,
            beforeLevel = gd?.rank ?? 0,
            afterLevel = gd?.rank ?? 0
        };
    }

    private static UserLivePoint BuildUserLivePoint(MasterBoost boost) =>
        new()
        {
            addNormalProgress = boost.livePointRate,
            addDailyBonusProgress = 0,
            livePointBonusRemaining = boost.costBoost,
            liveMissionPeriodId = Master.GetCurrentLiveMissionPeriodId()
        };

    private void UpdateLiveMissionProgress(UserLivePoint livePoint)
    {
        if (livePoint.liveMissionPeriodId <= 0 || livePoint.addNormalProgress <= 0)
            return;

        Data.userLiveMissions ??= [];
        var missions = Data.userLiveMissions.ToList();
        var mission = missions.FirstOrDefault(m =>
            m.liveMissionPeriodId == livePoint.liveMissionPeriodId &&
            string.Equals(m.liveMissionStatus, "free", StringComparison.Ordinal));

        if (mission == null)
        {
            mission = new UserLiveMission
            {
                userId = GetUserId(),
                liveMissionPeriodId = livePoint.liveMissionPeriodId,
                liveMissionStatus = "free",
                achievedMissionIds = []
            };
            missions.Add(mission);
        }

        mission.progress += livePoint.addNormalProgress;
        mission.achievedMissionIds ??= [];
        Data.userLiveMissions = missions.ToArray();
        UpdateRefreshableType(nameof(SuiteUser.userLiveMissions));
    }

    private static string BuildPlayResult(bool fullCombo, bool fullPerfect, int life)
    {
        if (life <= 0)
            return "not_clear";
        if (fullPerfect)
            return "full_perfect";
        if (fullCombo)
            return "full_combo";
        return "clear";
    }

    private static readonly Random Rng = new();

    private static string GenerateRandomString(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var result = new char[length];
        for (int i = 0; i < length; i++)
            result[i] = chars[Rng.Next(chars.Length)];
        return new string(result);
    }

    public GameUser DeepClone()
    {
        var bytes = MessagePackSerializer.Serialize(Data);
        var clonedData = MessagePackSerializer.Deserialize<SuiteUser>(bytes);
        var user = new GameUser(clonedData)
        {
            NotSuite = new NotSuiteData
            {
                InheritId = NotSuite.InheritId,
                InheritPassword = NotSuite.InheritPassword,
                PresentHistories = [.. NotSuite.PresentHistories],
                UserLiveSessions = NotSuite.UserLiveSessions.ToDictionary(
                    kv => kv.Key,
                    kv => new UserLiveSessionData
                    {
                        UserLiveId = kv.Value.UserLiveId,
                        MusicId = kv.Value.MusicId,
                        MusicDifficultyId = kv.Value.MusicDifficultyId,
                        MusicVocalId = kv.Value.MusicVocalId,
                        DeckId = kv.Value.DeckId,
                        BoostCount = kv.Value.BoostCount,
                        IsAuto = kv.Value.IsAuto,
                        MusicCategoryName = kv.Value.MusicCategoryName,
                        CustomMusicScoreId = kv.Value.CustomMusicScoreId,
                        CreatedAt = kv.Value.CreatedAt
                    })
            }
        };
        return user;
    }
}
