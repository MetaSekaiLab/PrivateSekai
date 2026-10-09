extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Shop;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class StampShopChecks
{
    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/stamp-shop", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "shopItems.json"),
                """[{"id":1,"shopId":12,"resourceBoxId":1,"costs":[{"cost":{"resourceType":"material","resourceId":44,"quantity":1}}]}]""");
            File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"),
                """[{"id":1,"resourceBoxPurpose":"shop_item","details":[{"resourceType":"stamp","resourceId":38,"resourceQuantity":1}]}]""");
            File.WriteAllText(Path.Combine(directory, "stamps.json"),
                """[{"id":38,"stampType":"illustration","characterId1":1}]""");
            File.WriteAllText(Path.Combine(directory, "honorMissions.json"),
                """[{"id":1,"honorMissionType":"collect_stamp","requirement":30}]""");
            File.WriteAllText(Path.Combine(directory, "characterMissionV2s.json"),
                """[{"id":1004,"characterId":1,"characterMissionType":"collect_stamp","parameterGroupId":4}]""");
            File.WriteAllText(Path.Combine(directory, "characterMissionV2ParameterGroups.json"),
                """[{"id":4,"seq":1,"requirement":2}]""");
            var store = new MemoryUserStore();
            using var provider = new ServiceCollection().AddPrivateSekai()
                .AddSingleton(_ => new CustomProfileThumbnailStore())
                .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
                .AddSingleton<IUserStore>(store).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
            var user = scope.ServiceProvider.GetRequiredService<UserSession>();
            var shop = scope.ServiceProvider.GetRequiredService<ShopService>();
            var state = TestUsers.Create(1);
            state.Data.userMaterials = [new() { materialId = 44, quantity = 1 }];
            state.Data.userStamps = [];
            state.Data.userShops = [new() { shopId = 12, userShopItems = [new() { shopItemId = 1, status = "sale" }] }];
            state.Data.userHonorMissions = [new() { honorMissionType = "collect_stamp", progress = 29, achievedMissionIds = [] }];
            state.Data.userCharacterMissions = [new() { characterId = 1, characterMissionType = "collect_stamp", progress = 1, achievedMissions = [] }];
            state.Data.userCharacterMissionStatuses = [];
            store.Save(1, state);
            Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
            {
                shop.PurchaseShopItem(12, 1);
                return new BrokenResponse();
            }), "表情购买响应编码失败时回滚");
            var before = store.Read(1)!.Data;
            Check.That(before.userMaterials.Single().quantity == 1 && before.userStamps.Length == 0 &&
                before.userShops.Single().userShopItems.Single().status == "sale" &&
                before.userCharacterMissions.Single().progress == 1 && before.userHonorMissions.Single().progress == 29,
                "扣券、表情、商品和任务共同回滚");
            var bytes = operation.Execute(1, () =>
            {
                var result = shop.PurchaseShopItem(12, 1);
                Check.That(result.Status == 200 && result.Achieved.Single().missionId == 1004, "表情收集跨门槛返回当次达成项");
                return user.BuildRefresh(result.ExcludeShop ? [nameof(SuiteUser.userShops)] : null);
            });
            var refresh = DumpSerializer.Deserialize<SuiteUser>(bytes);
            var saved = store.Read(1)!.Data;
            Check.That(saved.userMaterials.Single().quantity == 0 && saved.userStamps.Single().stampId == 38 &&
                saved.userShops.Single().userShopItems.Single().status == "sold_out", "表情购买扣券、授予并持久化售罄");
            Check.That(saved.userCharacterMissions.Single().progress == 2 && saved.userHonorMissions.Single().progress == 30 &&
                saved.userCharacterMissions.Single().achievedMissions.Length == 0, "表情推进角色和荣誉收集，达成提示不持久化");
            Check.That(saved.userMissionStatuses.Single(s => s.missionType == "honor_mission" && s.missionId == 1).missionStatus == "achieved",
                "表情购买跨称号门槛仍扣券和发货，同时持久化达成状态");
            Check.That(refresh.userShops == null && refresh.userStamps.Length == 1, "表情购买响应刷新持有列表并省略商店列表");
            operation.Execute(1, () =>
            {
                Check.That(shop.PurchaseShopItem(12, 1).Status == 409, "售罄表情重复购买拒绝");
                return null;
            });
            state = store.Read(1)!;
            state.Data.userStamps = [];
            state.Data.userShops.Single().userShopItems.Single().status = "sale";
            store.Save(1, state);
            operation.Execute(1, () =>
            {
                Check.That(shop.PurchaseShopItem(12, 1).Status == 409, "缺少兑换券时拒绝表情购买");
                return null;
            });
            Check.That(store.Read(1)!.Data.userStamps.Length == 0 && store.Read(1)!.Data.userHonorMissions.Single().progress == 30,
                "失败购买不新增表情或任务进度");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
