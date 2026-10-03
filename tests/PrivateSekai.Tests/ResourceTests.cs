extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Gacha;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Music;
using PrivateSekai.Modules.Mysekai;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class ResourceTests
{
    public static void Run()
    {
        var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/resource-fixtures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(fixture);
        try
        {
            File.WriteAllText(Path.Combine(fixture, "cardEpisodes.json"), """
                [{"id":701,"cardId":7},{"id":702,"cardId":7}]
                """);
            File.WriteAllText(Path.Combine(fixture, "musicVocals.json"), """
                [{"id":71,"musicId":7,"releaseConditionId":5},{"id":72,"musicId":7,"releaseConditionId":9},{"id":81,"musicId":8,"releaseConditionId":5}]
                """);
            File.WriteAllText(Path.Combine(fixture, "mysekaiTools.json"), """
                [{"id":4,"maxDurability":12}]
                """);
            var master = new MasterData(new MasterCacheConfig { PinTables = [] }, fixture);
            VerifyResources(master);
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
        }
    }

    private static void VerifyResources(MasterData master)
    {
        var state = TestUsers.Create(1);
        state.Data.userChargedCurrency = new ChargedCurrency { free = 50, paid = 100, paidUnitPrices = [] };
        var store = new MemoryUserStore();
        store.Save(1, state);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var queries = new ResourceMasterQueries(master);
        IResourceHandler[] handlers =
        [
            new CurrencyResourceHandler(), new InventoryResourceHandler(), new GachaResourceHandler(),
            new CardResourceHandler(new CardMasterQueries(master, queries)),
            new ProfileResourceHandler(), new MusicResourceHandler(queries), new MysekaiResourceHandler(queries)
        ];
        var resources = new ResourceService(session, handlers);
        Check.Throws<InvalidOperationException>(
            () => new ResourceService(session, [new CurrencyResourceHandler(), new CurrencyResourceHandler()]),
            "重复资源类型在构造时失败");

        var response = operation.Execute(1, () =>
        {
            VerifyCurrency(session, resources);
            VerifyInventory(session, resources);
            VerifyOwnership(session, resources);
            Check.Throws<NotSupportedException>(() => resources.Grant(Reward("unimplemented", 1)), "未知奖励类型明确失败");
            Check.Throws<NotSupportedException>(() => resources.Consume(null, 0, 1), "缺失消耗类型明确失败");
            Check.Throws<NotSupportedException>(() => resources.Grant(Reward("paid_jewel", 1)), "未实现付费水晶授予明确失败");
            Check.Throws<NotSupportedException>(() => resources.Consume("card", 7, 1), "授予型资源不可被通用扣除");
            var boostQuantity = session.Data.userBoostItems.Single(i => i.boostItemId == 2).quantity;
            Check.That(resources.Consume("boost_item", 2, 1) == boostQuantity - 1, "体力道具通过资源服务扣除库存");
            return session.BuildRefresh();
        });

        var refresh = DumpSerializer.Deserialize<SuiteUser>(response);
        Check.That(refresh.userCards?.Length == 1 && refresh.userMaterials?.Length == 1 &&
                   refresh.userChargedCurrency != null && refresh.userCostume3dStatuses?.Length == 1 &&
                   refresh.userMusicVocals?.Count == 2 && refresh.userMysekaiTools?.Length == 1,
            "跨模块资源变化统一写入刷新响应");
        var saved = store.Read(1)!.Data;
        Check.That(saved.userCards![0].duplicateCount == 4 && saved.userCards[0].episodes[0].scenarioStatus == "already_read" &&
                   saved.userCards[0].episodes[0].isNotSkipped && saved.userGachaTickets![0].quantity == 3,
            "资源结果通过用户操作持久化到独立快照");
    }

    private static void VerifyCurrency(UserSession session, ResourceService resources)
    {
        Check.That(resources.Consume("jewel", 0, 60) == 90 &&
                   session.Data.userChargedCurrency!.free == 0 && session.Data.userChargedCurrency.paid == 90,
            "普通水晶消耗优先免费余额并返回总余额");
        session.Data.userChargedCurrency = new ChargedCurrency { free = 50, paid = 100, paidUnitPrices = [] };
        Check.That(resources.Consume("jewel", 0, 60, paidFirst: true) == 90 &&
                   session.Data.userChargedCurrency.free == 50 && session.Data.userChargedCurrency.paid == 40,
            "指定付费优先时保留免费余额");
        Check.That(resources.Consume("paid_jewel", 0, 60) == 0 && session.Data.userChargedCurrency.free == 50,
            "付费水晶仅扣付费余额并保持扣至零行为");
        resources.Grant(Reward("jewel", 1, 7));
        Check.That(session.Data.userChargedCurrency.free == 57, "水晶奖励进入免费余额");
        Check.That(resources.Consume("coin", 0, 1000) == 0 && session.Data.userGamedata!.coin == 0,
            "金币消耗保持原扣至零行为");
        resources.Grant(Reward("coin", 0, 11));
        resources.Grant(Reward("virtual_coin", 0, 9));
        Check.That(session.Data.userGamedata!.coin == 11 && session.Data.userGamedata.virtualCoin == 9,
            "金币及虚拟金币奖励分别入账");
        resources.Grant(Reward("jewel", 0, -1));
        Check.That(resources.Consume("jewel", 0, 0) == 0 && session.Data.userChargedCurrency.free == 57,
            "非正资源数量保持无操作");
    }

    private static void VerifyInventory(UserSession session, ResourceService resources)
    {
        resources.Grant(Reward("material", 2, 5));
        Check.That(resources.Consume("material", 2, 8) == 0, "材料扣除保持下限零");
        resources.Grant(Reward("practice_ticket", 2, 6));
        Check.That(resources.Consume("practice_ticket", 2, 2) == 4, "练习券收支共用记录");
        resources.Grant(Reward("gacha_ticket", 3, 5));
        Check.That(resources.Consume("gacha_ticket", 3, 2) == 3, "抽卡券返回扣除后余额");
        resources.Grant(Reward("gacha_ceil_item", 3, 7));
        Check.That(resources.Consume("gacha_ceil_item", 3, 10) == 0, "天井道具扣除保持下限零");
        resources.Grant(Reward("boost_item", 2, 4));
        Check.That(session.Data.userBoostItems![0].quantity == 4, "体力道具奖励保存数量");
        Check.That(session.Data.userPracticeTickets![0].userId == session.UserId &&
                   session.Data.userGachaTickets![0].userId == session.UserId &&
                   session.Data.userGachaCeilItems![0].userId == session.UserId,
            "新建库存记录保留所属用户");
        resources.Grant(Reward("material", 0, 9));
        Check.That(session.Data.userMaterials!.All(m => m.materialId != 0), "无效材料ID保持无操作");
    }

    private static void VerifyOwnership(UserSession session, ResourceService resources)
    {
        resources.Grant(Reward("card", 7, 3));
        resources.Grant(Reward("card", 7));
        var card = session.Data.userCards!.Single();
        Check.That(card.cardId == 7 && card.level == 1 && card.skillLevel == 1 && card.duplicateCount == 3 &&
                   card.createdAt == session.Now && card.specialTrainingStatus == "not_doing" && card.defaultImage == "original",
            "批量授卡初始化一次并正确累计重复数量");
        Check.That(card.episodes![0].cardEpisodeId == 701 && card.episodes[0].scenarioStatus == "unreleased" &&
                   card.episodes[0].scenarioStatusReasons.Length == 0 &&
                   card.episodes[1].cardEpisodeId == 702 && card.episodes[1].scenarioStatus == "can_not_read",
            "新卡使用 master 剧情映射及合法的未解锁状态");
        card.episodes[0].scenarioStatus = "already_read";
        card.episodes[0].isNotSkipped = true;
        resources.Grant(Reward("card", 7));
        Check.That(card.duplicateCount == 4 && card.episodes[0].scenarioStatus == "already_read" &&
                   card.episodes[0].isNotSkipped, "重复授卡保留已有剧情进度");
        Check.That(session.Data.userCostume3dStatuses == null || session.Data.userCostume3dStatuses.Length == 0,
            "通用授卡不隐式发放抽卡服装奖励");
        resources.Grant(Reward("costume_3d", 5, 2));
        resources.Grant(Reward("costume_3d", 5));
        resources.Grant(Reward("avatar_motion", 5, 2));
        resources.Grant(Reward("avatar_motion", 5));
        Check.That(session.Data.userCostume3dStatuses!.Single().obtainedAt == session.Now &&
                   session.Data.userAvatarMotions!.Length == 1, "服装与动作重复授予保持唯一");
        resources.Grant(Reward("music", 7));
        Check.That(session.Data.userMusics!.Single().musicId == 7 &&
                   session.Data.userMusicVocals!.Single().musicVocalId == 71,
            "歌曲授予仅解锁本曲满足默认条件的音源");
        resources.Grant(Reward("music_vocal", 72));
        Check.That(session.Data.userMusicVocals!.Count == 2, "独立音源资源按master关联歌曲");
        resources.Grant(Reward("mysekai_item", 3, 2));
        resources.Grant(Reward("mysekai_tool", 4, 2));
        var tool = session.Data.userMysekaiTools!.Single();
        Check.That(tool.durability == 12, "新工具耐久读取所属master定义");
        tool.durability = 3;
        resources.Grant(Reward("mysekai_tool", 4));
        Check.That(session.Data.userMysekaiItems!.Single().lastObtainedAt == session.Now &&
                   tool.quantity == 3 && tool.durability == 3 && tool.lastObtainedAt == session.Now,
            "Mysekai追加资源更新数量与时间并保留现有工具耐久");
    }

    private static UserResource Reward(string type, int id, int quantity = 1) =>
        new() { resourceType = type, resourceId = id, quantity = quantity };
}
