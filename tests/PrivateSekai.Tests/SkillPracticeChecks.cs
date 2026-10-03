extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class SkillPracticeChecks
{
    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../obj/skill-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            // 等级及道具经验取自现有 master；卡牌和角色关系为独立测试数据。
            File.WriteAllText(Path.Combine(directory, "facilities.json"),
                """[{"id":1,"facilityType":"skill_practice","releaseConditionId":10030}]""");
            File.WriteAllText(Path.Combine(directory, "cards.json"),
                """[{"id":1,"characterId":1,"cardRarityType":"rarity_4"}]""");
            File.WriteAllText(Path.Combine(directory, "cardRarities.json"),
                """[{"cardRarityType":"rarity_4","maxSkillLevel":4}]""");
            File.WriteAllText(Path.Combine(directory, "levels.json"), """
                [{"levelType":"card_skill_4","level":1,"totalExp":0},
                 {"levelType":"card_skill_4","level":2,"totalExp":1000},
                 {"levelType":"card_skill_4","level":3,"totalExp":4000},
                 {"levelType":"card_skill_4","level":4,"totalExp":10000}]
                """);
            File.WriteAllText(Path.Combine(directory, "skillPracticeTickets.json"), """
                [{"id":3,"exp":250},{"id":10103,"characterId":1,"exp":250},
                 {"id":10203,"characterId":2,"exp":250}]
                """);
            File.WriteAllText(Path.Combine(directory, "cardSkillCosts.json"), """
                [{"id":1,"materialId":15,"exp":1},
                 {"id":29,"materialId":127,"unit":"light_sound","exp":1},
                 {"id":30,"materialId":128,"unit":"idol","exp":1}]
                """);
            File.WriteAllText(Path.Combine(directory, "gameCharacters.json"),
                """[{"id":1,"unit":"light_sound"}]""");
            var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
            var store = new MemoryUserStore();
            using var provider = new ServiceCollection().AddPrivateSekai()
                .AddSingleton(_ => new PrivateSekai.Storage.CustomProfileThumbnailStore())
                .AddSingleton(master).AddSingleton<IUserStore>(store).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            var cards = scope.ServiceProvider.GetRequiredService<CardService>();
            var resources = scope.ServiceProvider.GetRequiredService<ResourceService>();
            var state = TestUsers.Create(1);
            state.Data.userCards = [new UserCard { cardId = 1, skillLevel = 1, totalSkillExp = 900, skillExp = 900 }];
            state.Data.userMaterials = [new UserMaterial { materialId = 15, quantity = 10000 },
                new UserMaterial { materialId = 127, quantity = 100 }, new UserMaterial { materialId = 128, quantity = 100 }];
            state.Data.refreshableTypes = ["userCards"];
            store.Save(1, state);
            var controller = new CardController(operations, user, cards)
            {
                ControllerContext = new() { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
            };
            var materialError = (Microsoft.AspNetCore.Mvc.FileContentResult)controller.HandleSkillPracticeMaterial(1, 1,
                new() { costs = [Cost("material", 15, 1)] });
            var materialErrorBody = DumpSerializer.Deserialize<ClientErrorResponse>(materialError.FileContents);
            Check.That(controller.Response.StatusCode == 409 && materialErrorBody.HttpStatus == 409 &&
                materialErrorBody.ErrorCode == "" && materialErrorBody.ErrorMessage == "", "未解锁材料升级返回官方错误模型及 409");
            controller.Response.StatusCode = 200;
            var ticketError = (Microsoft.AspNetCore.Mvc.FileContentResult)controller.HandleSkillPracticeTicket(1, 1,
                new() { costs = [Cost("skill_practice_ticket", 3, 1)] });
            var ticketErrorBody = DumpSerializer.Deserialize<ClientErrorResponse>(ticketError.FileContents);
            Check.That(controller.Response.StatusCode == 409 && ticketErrorBody.HttpStatus == 409 &&
                ticketErrorBody.ErrorCode == "" && ticketErrorBody.ErrorMessage == "", "未解锁技能券升级返回官方错误模型及 409");
            Check.That(store.Read(1)!.Data.userCards.Single().totalSkillExp == 900 &&
                store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 15).quantity == 10000,
                "未解锁请求不扣材、不改变卡牌经验");
            Check.That(store.Read(1)!.Data.refreshableTypes.SequenceEqual(["userCards"]),
                "未解锁拒绝不消耗原有待刷新字段");
            state.Data.userReleaseConditions = [new() { releaseConditionId = 10030 }];
            store.Save(1, state);
            operations.Execute(1, () =>
            {
                resources.Grant(Cost("skill_practice_ticket", 3, 4));
                resources.Grant(Cost("skill_practice_ticket", 10103, 2));
                resources.Grant(Cost("skill_practice_ticket", 10203, 2));
                return user.BuildRefresh();
            });

            var ticketBytes = operations.Execute(1, () => new UserCardSkillPracticeTicketResponse
            {
                updateExpResult = cards.PracticeCardSkill(1, [Cost("skill_practice_ticket", 3, 1)], "skill_practice_ticket"),
                updatedResources = user.BuildRefresh()
            });
            var ticketResult = DumpSerializer.Deserialize<UserCardSkillPracticeTicketResponse>(ticketBytes);
            Check.That(ticketResult.updateExpResult.beforeTotalExp == 900 && ticketResult.updateExpResult.afterTotalExp == 1150 &&
                ticketResult.updateExpResult.afterLevel == 2 && ticketResult.updateExpResult.afterExp == 150 &&
                ticketResult.updatedResources.userSkillPracticeTickets.Single(t => t.skillPracticeTicketId == 3).quantity == 3,
                "技能券跨级更新经验、等级和库存，响应使用 dump 契约");

            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
                cards.PracticeCardSkill(1, [Cost("skill_practice_ticket", 10203, 1)], "skill_practice_ticket")),
                "角色限定技能券不可用于其他角色");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
                cards.PracticeCardSkill(1, [Cost("material", 128, 1)], "material")), "组合材料检查角色所属组合");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
                cards.PracticeCardSkill(2, [Cost("material", 15, 1)], "material")), "技能升级不能创建未持有卡牌");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
                cards.PracticeCardSkill(1, [Cost("skill_practice_ticket", 3, 2), Cost("skill_practice_ticket", 3, 2)], "skill_practice_ticket")),
                "重复消耗项合并检查余额");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
                cards.PracticeCardSkill(1, [Cost("material", 15, -1)], "material")), "拒绝负数消耗");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
                cards.PracticeCardSkill(1, [Cost("material", 999, 1)], "material")), "未知材料不回退到默认经验");
            Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
            {
                cards.PracticeCardSkill(1, [Cost("skill_practice_ticket", 10103, 1)], "skill_practice_ticket");
                return new BrokenResponse();
            }), "编码失败回滚技能升级");
            Check.That(store.Read(1)!.Data.userCards.Single().totalSkillExp == 1150 &&
                store.Read(1)!.Data.userSkillPracticeTickets.Single(t => t.skillPracticeTicketId == 10103).quantity == 2,
                "失败操作不扣券、不增加经验");

            var materialBytes = operations.Execute(1, () => new UserCardSkillPracticeMaterialResponse
            {
                updateExpResult = cards.PracticeCardSkill(1, [Cost("material", 127, 100), Cost("material", 15, 10000)], "material"),
                updatedResources = user.BuildRefresh()
            });
            var materialResult = DumpSerializer.Deserialize<UserCardSkillPracticeMaterialResponse>(materialBytes);
            Check.That(materialResult.updateExpResult.afterTotalExp == 10000 && materialResult.updateExpResult.afterLevel == 4 &&
                materialResult.updateExpResult.afterExp == 0 && materialResult.updatedResources.userMaterials
                    .Where(m => m.materialId is 15 or 127).All(m => m.quantity == 0),
                "材料溢出经验截至满级，完整消耗投入数量");
            Check.Throws<ArgumentException>(() => operations.Execute(1, () =>
                cards.PracticeCardSkill(1, [Cost("skill_practice_ticket", 3, 1)], "skill_practice_ticket")), "满级不继续扣券");
            Console.WriteLine("技能升级：技能券、材料、适用范围、跨级、满级及失败回滚检查通过。");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static UserResource Cost(string type, int id, int quantity) =>
        new() { resourceType = type, resourceId = id, quantity = quantity };
}
