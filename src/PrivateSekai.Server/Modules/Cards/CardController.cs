extern alias game;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Models;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Cards;

public sealed class CardController(UserOperation operations, UserSession user, CardService cards) : PrskController
{
    [HttpPost("api/user/{userId}/card/{cardId}/skill-practice-ticket")]
    public IActionResult HandleSkillPracticeTicket(long userId, int cardId,
        [FromBody] UserCardSkillPracticeTicketRequest request) =>
        Encoded(operations.Execute(userId, () => new UserCardSkillPracticeTicketResponse
        {
            updateExpResult = cards.PracticeCardSkill(cardId, request.costs, "skill_practice_ticket"),
            updatedResources = user.BuildRefresh()
        }));

    [HttpPost("api/user/{userId}/card/{cardId}/material")]
    public IActionResult HandleSkillPracticeMaterial(long userId, int cardId,
        [FromBody] UserCardSkillPracticeMaterialRequest request) =>
        Encoded(operations.Execute(userId, () => new UserCardSkillPracticeMaterialResponse
        {
            updateExpResult = cards.PracticeCardSkill(cardId, request.costs, "material"),
            updatedResources = user.BuildRefresh()
        }));

    /// <summary>
    /// 执行等待室卡牌转换。客户端把确认转换的卡牌汇总成 `userCards` 发给服务端，请求成功后合并返回的用户资源差异，并刷新等待室显示。
    /// </summary>
    [HttpPut("api/user/{userId}/card")]
    public IActionResult HandleCard(long userId, [FromQuery] string? behavior, [FromBody] UserCardExchangeRequest request)
    {
        if (!string.Equals(behavior, "exchange", StringComparison.Ordinal))
            return NotFound();

        return Encoded(operations.Execute(userId, () =>
        {
            cards.ExchangeCards(request.userCards);

            return new UserCardExchangeResponse
            {
                updatedResources = user.BuildRefresh()
            };
        }));
    }

    /// <summary>
    /// 使用练习券提升卡牌等级。客户端把目标卡牌写入 path，并提交本次消耗的练习券列表；成功后使用返回的经验变化结果播放练习结果。
    /// </summary>
    [HttpPost("api/user/{userId}/card/{cardId}/practice-ticket")]
    public IActionResult HandleCardPracticeTicket(
        long userId,
        int cardId,
        [FromBody] UserCardPracticeTicketRequest request)
    {
        return Encoded(operations.Execute(userId, () =>
        {
            var achievedBefore = (user.Data.userMissionStatuses ?? [])
                .Where(s => s.missionType == "beginner_mission_v2" && s.missionStatus is "achieved" or "received")
                .Select(s => s.missionId).ToHashSet();
            var response = cards.PracticeCardWithTickets(cardId, request.costs);
            response.updatedResources = user.BuildRefresh();
            if (response.updatedResources.userBeginnerMissionV2s != null)
            {
                var newlyAchieved = (user.Data.userMissionStatuses ?? [])
                    .Where(s => s.missionType == "beginner_mission_v2" && s.missionStatus == "achieved" && !achievedBefore.Contains(s.missionId))
                    .Select(s => s.missionId).ToHashSet();
                response.updatedResources.userBeginnerMissionV2s = response.updatedResources.userBeginnerMissionV2s
                    .Select(m => new UserBeginnerMissionV2
                    {
                        beginnerMissionV2Id = m.beginnerMissionV2Id, progress = m.progress,
                        isNewAchieved = newlyAchieved.Contains(m.beginnerMissionV2Id)
                    }).ToArray();
            }
            return response;
        }));
    }

    /// <summary>
    /// 执行卡牌 Master Lesson。客户端提交本次消耗的 Master Lesson cost ID，成功后合并用户资源并展示获得奖励。
    /// </summary>
    [HttpPost("api/user/{userId}/card/{cardId}/master-lesson")]
    public IActionResult HandleCardMasterLesson(
        long userId,
        int cardId,
        [FromBody] PostUserCardMasterLessonAPIRequest request)
    {
        return Encoded(operations.Execute(userId, () =>
        {
            var response = cards.MasterLessonCard(cardId, request.MasterLessonCostIds);
            response.updatedResources = user.BuildRefresh();
            return response;
        }));
    }

    /// <summary>
    /// 更新单张卡牌的培养相关状态。当前确认的行为包括特训完成状态和默认显示立绘。
    /// </summary>
    [HttpPut("api/user/{userId}/card/{cardId}")]
    public async Task<IActionResult> HandleCardBehavior(
        long userId,
        int cardId,
        [FromQuery] string? behavior)
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        var request = ms.ToArray();

        if (behavior is not ("special_training" or "set_default_image"))
            return NotFound();

        var alreadyTrained = false;
        var encoded = operations.Execute(userId, () =>
        {
            switch (behavior)
            {
                case "special_training":
                    var specialTrainingRequest =
                        DumpSerializer.Deserialize<UserCardSpecialTrainingRequest>(request);
                    var before = (user.Data.userCharacterMissionStatuses ?? [])
                        .Select(s => (s.characterId, s.missionId, s.parameterGroupId, s.seq)).ToHashSet();
                    var rewards = cards.SetCardSpecialTrainingStatus(cardId, specialTrainingRequest?.specialTrainingStatus);
                    if (rewards == null)
                    {
                        alreadyTrained = true;
                        return null;
                    }
                    var achieved = (user.Data.userCharacterMissionStatuses ?? [])
                        .Where(s => !before.Contains((s.characterId, s.missionId, s.parameterGroupId, s.seq))).ToArray();
                    var refresh = user.BuildRefresh();
                    if (refresh.userCharacterMissions != null)
                        refresh.userCharacterMissions = refresh.userCharacterMissions.Select(m => new UserCharacterMissionV2
                        {
                            userId = m.userId, characterId = m.characterId, characterMissionType = m.characterMissionType,
                            progress = m.progress, achievedMissions = m.characterMissionType == "collect_member"
                                ? achieved.Where(s => s.characterId == m.characterId).ToArray() : []
                        }).ToArray();
                    // 无特殊奖励的官方样本返回空数组；非空奖励字段仍待核验。
                    return (object)new SpecialTrainingResponse { UpdatedResources = refresh, IncludeEmptyResources = rewards.Length == 0 };
                case "set_default_image":
                    var defaultImageRequest =
                        DumpSerializer.Deserialize<UserCardDefaultImageRequest>(request);
                    cards.SetCardDefaultImage(cardId, defaultImageRequest?.defaultImage);
                    break;
            }

            return new SuiteUserCommonResponse
            {
                updatedResources = user.BuildRefresh()
            };
        });
        return alreadyTrained ? StatusCode(409) : Encoded(encoded);
    }
}
