extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Shop;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class AreaShopChecks
{
    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/area-shop", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "areaItems.json"), """[{"id":1,"areaId":5}]""");
            File.WriteAllText(Path.Combine(directory, "characterMissionV2AreaItems.json"),
                """[{"areaItemId":1,"characterId":1,"characterMissionType":"area_item_level_up_character"}]""");
            File.WriteAllText(Path.Combine(directory, "characterMissionV2s.json"),
                """[{"id":1009,"characterId":1,"characterMissionType":"area_item_level_up_character","parameterGroupId":9}]""");
            File.WriteAllText(Path.Combine(directory, "characterMissionV2ParameterGroups.json"),
                """[{"id":9,"seq":1,"requirement":1},{"id":9,"seq":2,"requirement":2}]""");
            File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
                [{"id":10,"resourceBoxPurpose":"shop_item","details":[{"resourceType":"area_item","resourceId":1,"resourceLevel":1,"resourceQuantity":1}]},
                 {"id":20,"resourceBoxPurpose":"shop_item","details":[{"resourceType":"area_item","resourceId":1,"resourceLevel":2,"resourceQuantity":1}]},
                 {"id":30,"resourceBoxPurpose":"shop_item","details":[{"resourceType":"area_item","resourceId":1,"resourceLevel":3,"resourceQuantity":1}]}]
                """);
            File.WriteAllText(Path.Combine(directory, "shopItems.json"), """
                [{"id":100,"shopId":5,"resourceBoxId":10,"costs":[{"cost":{"resourceType":"material","resourceId":2,"quantity":10}}]},
                 {"id":700,"shopId":5,"resourceBoxId":20,"costs":[{"cost":{"resourceType":"material","resourceId":2,"quantity":20}}]},
                 {"id":900,"shopId":5,"resourceBoxId":30,"costs":[{"cost":{"resourceType":"material","resourceId":2,"quantity":30}}]}]
                """);
            var store = new MemoryUserStore();
            using var provider = new ServiceCollection().AddPrivateSekai()
                .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
                .AddSingleton<IUserStore>(store).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var shop = scope.ServiceProvider.GetRequiredService<ShopService>();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            var state = TestUsers.Create(1);
            state.Data.userMaterials = [new() { materialId = 2, quantity = 30 }];
            state.Data.userAreas = [new() { areaId = 5, areaItems = [] }];
            state.Data.userShops = [new() { shopId = 5, userShopItems = [new() { shopItemId = 100, level = 1, status = "sale" }] }];
            store.Save(1, state);
            Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
            {
                shop.PurchaseShopItem(5, 100);
                return new BrokenResponse();
            }), "区域道具响应编码失败时回滚");
            Check.That(store.Read(1)!.Data.userMaterials.Single().quantity == 30 &&
                store.Read(1)!.Data.userAreas.Single().areaItems.Length == 0 &&
                store.Read(1)!.Data.userShops.Single().userShopItems.Single().shopItemId == 100,
                "区域道具、材料和下一等级商品共同回滚");
            Check.That(store.Read(1)!.Data.userCharacterMissions?.Any(m => m.characterMissionType == "area_item_level_up_character") != true,
                "失败购买不留下角色任务进度");
            operation.Execute(1, () => { shop.PurchaseShopItem(5, 100); return user.BuildRefresh(); });
            Check.Throws<ArgumentException>(() => operation.Execute(1, () =>
            {
                shop.PurchaseShopItem(5, 100);
                return user.BuildRefresh();
            }), "已替换的区域商品不能再次购买");
            Check.That(store.Read(1)!.Data.userShops.Single().userShopItems.Single().shopItemId == 700 &&
                store.Read(1)!.Data.userAreas.Single().areaItems.Single().level == 1,
                "区域商品通过奖励盒关联下一级，不使用商品 ID 加一");
            operation.Execute(1, () => { shop.PurchaseShopItem(5, 700); return user.BuildRefresh(); });
            Check.That(store.Read(1)!.Data.userAreas.Single().areaItems.Single().level == 2 &&
                store.Read(1)!.Data.userMaterials.Single().quantity == 0 &&
                store.Read(1)!.Data.userShops.Single().userShopItems.Single().shopItemId == 900,
                "普通升级更新同一道具并替换商品，精确扣除材料");
            Check.That(store.Read(1)!.Data.userCharacterMissions.Single(m => m.characterMissionType == "area_item_level_up_character").progress == 2 &&
                store.Read(1)!.Data.userCharacterMissionStatuses.Count(s => s.missionId == 1009) == 2 &&
                store.Read(1)!.Data.userCharacterMissions.Single(m => m.characterMissionType == "area_item_level_up_character").achievedMissions.Length == 0,
                "购买与升级各推进一次角色任务，达成提示不保存在进度内");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
