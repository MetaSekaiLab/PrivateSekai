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

internal static class VocalShopChecks
{
    public static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/vocal-shop", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "shopItems.json"), """
                [{"id":1,"shopId":4,"resourceBoxId":1,"costs":[
                  {"cost":{"resourceType":"material","resourceId":18,"quantity":1}},
                  {"cost":{"resourceType":"material","resourceId":20,"quantity":1}}]},
                 {"id":2,"shopId":4,"resourceBoxId":2,"costs":[
                  {"cost":{"resourceType":"material","resourceId":38,"quantity":2}}]}]
                """);
            File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), """
                [{"id":1,"resourceBoxPurpose":"shop_item","details":[{"resourceType":"music_vocal","resourceId":72,"resourceQuantity":1}]},
                 {"id":2,"resourceBoxPurpose":"shop_item","details":[{"resourceType":"music_vocal","resourceId":73,"resourceQuantity":1}]}]
                """);
            File.WriteAllText(Path.Combine(directory, "musicVocals.json"), """
                [{"id":72,"musicId":7,"musicVocalType":"another_vocal","characters":[
                  {"characterType":"game_character","characterId":1},{"characterType":"game_character","characterId":3}]},
                 {"id":73,"musicId":8,"musicVocalType":"another_vocal","characters":[{"characterType":"game_character","characterId":21}]}]
                """);
            File.WriteAllText(Path.Combine(directory, "honorMissions.json"),
                """[{"id":501,"honorMissionType":"collect_another_vocal","requirement":10}]""");
            File.WriteAllText(Path.Combine(directory, "characterMissionV2s.json"), """
                [{"id":1008,"characterId":1,"characterMissionType":"collect_another_vocal","parameterGroupId":8},
                 {"id":3008,"characterId":3,"characterMissionType":"collect_another_vocal","parameterGroupId":8},
                 {"id":21008,"characterId":21,"characterMissionType":"collect_another_vocal","parameterGroupId":8}]
                """);
            File.WriteAllText(Path.Combine(directory, "characterMissionV2ParameterGroups.json"),
                """[{"id":8,"seq":1,"requirement":1}]""");
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
            state.Data.userMaterials = [new() { materialId = 18, quantity = 2 }, new() { materialId = 20, quantity = 0 },
                new() { materialId = 38, quantity = 2 }];
            state.Data.userMusics = [];
            state.Data.userMusicVocals = [];
            state.Data.userShops = [new() { shopId = 4, userShopItems = [new() { shopItemId = 1, status = "sale" }, new() { shopItemId = 2, status = "sale" }] }];
            state.Data.userCharacterMissions = [];
            state.Data.userCharacterMissionStatuses = [];
            state.Data.userHonorMissions = [];
            store.Save(1, state);
            operation.Execute(1, () =>
            {
                Check.That(shop.PurchaseShopItem(4, 1).Status == 409, "合唱版本缺少第二种兑换券时拒绝");
                return null;
            });
            Check.That(store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 18).quantity == 2 &&
                store.Read(1)!.Data.userMusicVocals.Count == 0, "券不足不扣其他材料或授予 Vocal");
            state = store.Read(1)!;
            state.Data.userMaterials.Single(m => m.materialId == 20).quantity = 2;
            store.Save(1, state);
            Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
            {
                shop.PurchaseShopItem(4, 1);
                return new BrokenResponse();
            }), "Vocal 购买响应编码失败时回滚");
            var saved = store.Read(1)!.Data;
            Check.That(saved.userMaterials.All(m => m.quantity == 2) && saved.userMusicVocals.Count == 0 &&
                saved.userHonorMissions.Length == 0 && saved.userCharacterMissions.Length == 0 &&
                saved.userShops.Single().userShopItems.All(i => i.status == "sale"), "券、音源、商店和任务共同回滚");
            var bytes = operation.Execute(1, () =>
            {
                var result = shop.PurchaseShopItem(4, 1);
                Check.That(result.Status == 200 && result.Achieved.Length == 2 && !result.ExcludeShop,
                    "合唱版本返回双方当次达成任务并刷新商店");
                return user.BuildRefresh();
            });
            var refresh = DumpSerializer.Deserialize<SuiteUser>(bytes);
            saved = store.Read(1)!.Data;
            Check.That(saved.userMusicVocals.Single().musicVocalId == 72 && saved.userMusicVocals.Single().musicId == 7 &&
                saved.userMusics.Length == 0 && saved.userShops.Single().userShopItems.Single(i => i.shopItemId == 1).status == "sold_out",
                "购买关联 Vocal 并售罄，不隐式解锁基础歌曲");
            Check.That(saved.userMaterials.Where(m => m.materialId is 18 or 20).All(m => m.quantity == 1) &&
                saved.userCharacterMissions.Length == 2 && saved.userCharacterMissions.All(m => m.progress == 1 && m.achievedMissions.Length == 0) &&
                saved.userHonorMissions.Single().progress == 1, "合唱扣双方各一张券，双方任务各一次，荣誉只计一次");
            Check.That(refresh.userShops != null && refresh.userMusicVocals.Count == 1 && refresh.userMusics == null,
                "Vocal 响应刷新商店与音源，不刷新未变化的基础歌曲");
            operation.Execute(1, () => { Check.That(shop.PurchaseShopItem(4, 1).Status == 409, "重复 Vocal 购买拒绝"); return null; });
            Check.That(store.Read(1)!.Data.userHonorMissions.Single().progress == 1 && store.Read(1)!.Data.userMusicVocals.Count == 1,
                "重复购买不增加音源或收集计数");
            operation.Execute(1, () => { Check.That(shop.PurchaseShopItem(4, 2).Status == 200, "单角色 Vocal 购买成功"); return user.BuildRefresh(); });
            saved = store.Read(1)!.Data;
            Check.That(saved.userMaterials.Single(m => m.materialId == 38).quantity == 0 && saved.userMusicVocals.Count == 2 &&
                saved.userCharacterMissions.Single(m => m.characterId == 21).progress == 1 && saved.userHonorMissions.Single().progress == 2,
                "单角色 Vocal 扣两张券并计入角色与荣誉收集");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
