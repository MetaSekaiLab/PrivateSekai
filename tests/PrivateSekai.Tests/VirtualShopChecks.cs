extern alias game;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Modules.Shop;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class VirtualShopChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "virtual-shop-fixture");
        Directory.CreateDirectory(directory);
        var types = new[] { "avatar_motion", "avatar_costume", "avatar_accessory", "avatar_coordinate", "avatar_skin_color", "penlight" };
        var items = types.Select((type, index) => new MasterVirtualShopItem
        {
            id = 10 + index, virtualShopId = 7, virtualShopItemType = type, resourceBoxId = 100 + index,
            costJewel = 100, costVirtualCoin = 150, costPaidJewel = 80
        }).Concat(new[]
        {
            new MasterVirtualShopItem { id = 20, virtualShopId = 7, resourceBoxId = 200, costJewel = 10000 },
            new MasterVirtualShopItem { id = 21, virtualShopId = 7, resourceBoxId = 101, costJewel = 10 },
            new MasterVirtualShopItem { id = 22, virtualShopId = 7, resourceBoxId = 201, costVirtualCoin = 20 },
            new MasterVirtualShopItem { id = 23, virtualShopId = 7, resourceBoxId = 100, costJewel = 100 }
        }).ToArray();
        File.WriteAllText(Path.Combine(directory, "virtualShops.json"), JsonSerializer.Serialize(new[]
        {
            new MasterVirtualShop { id = 7, virtualShopType = "other", virtualShopItems = items }
        }, DumpJson.Options));
        File.WriteAllText(Path.Combine(directory, "resourceBoxes.json"), JsonSerializer.Serialize(types.Select((type, index) => new
        {
            id = 100 + index, resourceBoxPurpose = "virtual_shop_item",
            details = new[] { new { resourceType = type, resourceId = 1000 + index, resourceQuantity = 1 } }
        }).Concat(new[]
        {
            new { id = 200, resourceBoxPurpose = "virtual_shop_item", details = new[] { new { resourceType = "avatar_motion", resourceId = 2000, resourceQuantity = 1 } } },
            new { id = 201, resourceBoxPurpose = "virtual_shop_item", details = new[] { new { resourceType = "avatar_accessory", resourceId = 2001, resourceQuantity = 1 } } }
        })));
        var master = new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
        var state = TestUsers.Create(1);
        state.Data.userChargedCurrency = new() { free = 150, paid = 200, paidUnitPrices = [] };
        state.Data.userGamedata.virtualCoin = 500;
        state.Data.userAvatar = new() { avatarSkinColorId = 1 };
        state.Data.userAvatarSkinColors = [new() { avatarSkinColorId = 1 }];
        state.Data.userVirtualShops = [new() { virtualShopId = 99, userVirtualShopItems =
            [new() { virtualShopId = 99, virtualShopItemId = 99, buyCount = 0, status = "sale" }] },
            new() { virtualShopId = 7, userVirtualShopItems = items.Select(i => new UserVirtualShopItem
                { virtualShopId = 7, virtualShopItemId = i.id, status = i.id == 21 ? "sold_out" : "sale" }).ToArray() }];
        var store = new MemoryUserStore();
        store.Save(1, state);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var resources = new ResourceService(user, [new CurrencyResourceHandler(), new ProfileResourceHandler()]);
        var service = new VirtualShopService(user, new ShopMasterQueries(master), new ResourceMasterQueries(master), resources);
        (int Status, string Error, byte[] Bytes) Buy(int id, string cost, int shop = 7)
        {
            var status = 0;
            var error = "";
            var bytes = operations.Execute(1, () =>
            {
                var result = service.Purchase(shop, id, cost);
                status = result.Status;
                error = result.ErrorCode;
                return status == 200 ? (object)new UserVirtualShopResponse
                {
                    updatedResources = user.BuildRefresh(), userResources = result.Rewards, obtainedFirstPurchaseBonusList = []
                } : new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = error, ErrorMessage = "" };
            });
            return (status, error, bytes);
        }
        var motion = Buy(10, "JEWEL");
        var first = DumpSerializer.Deserialize<UserVirtualShopResponse>(motion.Bytes);
        Check.That(motion.Status == 200 && first.userResources.Single().resourceType == "avatar_motion" &&
            store.Read(1)!.Data.userAvatarMotions.Single().avatarMotionId == 1000,
            "虚拟商店按master奖励发放动作");
        Check.That(store.Read(1)!.Data.userChargedCurrency.free == 50 && store.Read(1)!.Data.userChargedCurrency.paid == 200,
            "JEWEL优先消耗免费余额");
        Check.That(store.Read(1)!.Data.userVirtualShops[1].userVirtualShopItems.Single(i => i.virtualShopItemId == 23).status == "sold_out",
            "同一物品的其他普通商品入口同时售罄");
        Check.That(first.updatedResources.userVirtualShops.Select(s => s.virtualShopId).SequenceEqual(new[] { 99, 7 }) &&
            first.updatedResources.userVirtualShops[1].userVirtualShopItems[0].status == "sold_out" &&
            first.updatedResources.userVirtualShops[1].userVirtualShopItems[0].buyCount == null &&
            first.updatedResources.userVirtualShops[0].userVirtualShopItems[0].buyCount == 0,
            "普通购买刷新全部商店且保留原顺序、缺失次数和显式零次数");
        var json = JsonNode.Parse(MessagePackSerializer.ConvertToJson(motion.Bytes))!;
        Check.That(json["userResources"]![0]!["resourceLevel"] == null &&
            json["obtainedFirstPurchaseBonusList"]!.AsArray().Count == 0,
            "虚拟商店普通资源省略零等级，返回空首次赠品列表");
        Buy(11, "JEWEL");
        Check.That(store.Read(1)!.Data.userChargedCurrency.free == 0 && store.Read(1)!.Data.userChargedCurrency.paid == 150 &&
            store.Read(1)!.Data.userAvatarCostumes.Single().avatarCostumeId == 1001,
            "免费余额不足时补扣付费余额，服装到账");
        Buy(12, "PAID_JEWEL");
        Check.That(store.Read(1)!.Data.userChargedCurrency.paid == 70 &&
            store.Read(1)!.Data.userAvatarAccessories.Single().avatarAccessoryId == 1002,
            "付费专用消耗只扣付费余额并发放配件");
        Buy(13, "VIRTUAL_COIN");
        Buy(14, "VIRTUAL_COIN");
        Buy(15, "VIRTUAL_COIN");
        var owned = store.Read(1)!.Data;
        Check.That(owned.userGamedata.virtualCoin == 50 && owned.userChargedCurrency.paid == 70 &&
            owned.userAvatarCoordinates.Single().avatarCoordinateId == 1003 &&
            owned.userAvatarSkinColors.Select(c => c.avatarSkinColorId).SequenceEqual(new[] { 1, 1004 }) &&
            owned.userPenlights.Single().penlightId == 1005 && !owned.userPenlights[0].favoriteFlg,
            "虚拟币独立扣款，套装、肤色和荧光棒按对应资源发放");
        Check.That(owned.userAvatar.avatarSkinColorId == 1 && owned.userAvatar.avatarCostumeId == null,
            "购买不自动改变当前穿戴");
        var before = DumpSerializer.Serialize(owned);
        foreach (var item in new[] { (Id: 10, Type: "JEWEL", Status: 409, Error: ""),
            (Id: 20, Type: "JEWEL", Status: 409, Error: "not_enough_resource"),
            (Id: 20, Type: "PAID_JEWEL", Status: 409, Error: ""),
            (Id: 21, Type: "JEWEL", Status: 409, Error: ""),
            (Id: 23, Type: "JEWEL", Status: 409, Error: ""),
            (Id: 999, Type: "JEWEL", Status: 404, Error: "") })
        {
            var result = Buy(item.Id, item.Type);
            Check.That(result.Status == item.Status && result.Error == item.Error &&
                DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "重复、售罄、余额不足、未提供消耗和未知商品整体拒绝，无部分扣款");
        }
        Check.That(Buy(10, "JEWEL", 999).Status == 404, "未知虚拟商店返回404");
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            Check.That(service.Purchase(7, 22, "VIRTUAL_COIN").Status == 200, "回滚检查实际完成扣款和新配件发放");
            return new BrokenResponse();
        }), "虚拟商店响应编码失败回滚扣款、发放和售罄状态");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
            "编码失败后所有持久状态保持一致");
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N"), "users.db");
        using (var sql = new SqliteUserStore(path)) sql.Save(1, store.Read(1)!);
        using (var sql = new SqliteUserStore(path))
            Check.That(sql.Read(1)!.Data.userVirtualShops[1].userVirtualShopItems[0].buyCount == null &&
                sql.Read(1)!.Data.userVirtualShops[0].userVirtualShopItems[0].buyCount == 0,
                "SQLite重启分别保留缺失购买次数和显式零值");
    }
}
