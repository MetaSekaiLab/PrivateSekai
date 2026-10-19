extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class VirtualShopHttpChecks
{
    private const int ShopId = 99100;

    public static void WriteMaster(string directory)
    {
        var offers = new[]
        {
            new MasterVirtualShopItem { id = 1, virtualShopId = ShopId, resourceBoxId = 991001, costJewel = 50 },
            new MasterVirtualShopItem { id = 2, virtualShopId = ShopId, resourceBoxId = 991002, costVirtualCoin = 70 },
            new MasterVirtualShopItem { id = 3, virtualShopId = ShopId, resourceBoxId = 991003, costPaidJewel = 20 },
            new MasterVirtualShopItem { id = 4, virtualShopId = ShopId, resourceBoxId = 991001, costJewel = 50 },
            new MasterVirtualShopItem { id = 5, virtualShopId = ShopId, resourceBoxId = 991004, costJewel = 200 }
        };
        File.WriteAllText(Path.Combine(directory, "virtualShops.json"), JsonSerializer.Serialize(new[]
        {
            new MasterVirtualShop { id = ShopId, virtualShopType = "other", virtualShopItems = offers }
        }, DumpJson.Options));
        var path = Path.Combine(directory, "resourceBoxes.json");
        var boxes = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        var types = new[] { "avatar_costume", "avatar_accessory", "avatar_motion", "avatar_motion" };
        for (var i = 0; i < types.Length; i++)
            boxes.Add(JsonSerializer.SerializeToNode(new { id = 991001 + i, resourceBoxPurpose = "virtual_shop_item",
                details = new[] { new { resourceType = types[i], resourceId = 501 + i, resourceQuantity = 1 } } }));
        JsonFiles.Write(path, boxes);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userChargedCurrency = new() { free = 100, paid = 30, paidUnitPrices = [] };
        state.Data.userGamedata.virtualCoin = 100;
        state.Data.userAvatar = new() { avatarSkinColorId = 1 };
        state.Data.userAvatarCostumes = [];
        state.Data.userAvatarAccessories = [];
        state.Data.userAvatarMotions = [];
        state.Data.userVirtualShops = [new() { virtualShopId = ShopId, userVirtualShopItems = Enumerable.Range(1, 5)
            .Select(id => new UserVirtualShopItem { virtualShopId = ShopId, virtualShopItemId = id, status = "sale" }).ToArray() }];
        store.Save(1, state);
        ScenarioStep Buy(int id, string cost) => new() { Operation = "virtual-shop-purchase", Args = new()
            { ["virtualShopId"] = ShopId.ToString(), ["virtualShopItemId"] = id.ToString(), ["costResourceType"] = cost } };
        var scenario = new Scenario { Steps = [Buy(1, "JEWEL"), Buy(2, "VIRTUAL_COIN"), Buy(3, "PAID_JEWEL")] };
        var output = Path.Combine(directory, "virtual-shop");
        await ScenarioRunner.Run(client, scenario, output);
        var data = store.Read(1)!.Data;
        check(data.userChargedCurrency.free == 50 && data.userChargedCurrency.paid == 10 && data.userGamedata.virtualCoin == 30,
            "普通虚拟商店通过加密HTTP分别扣除免费、付费和虚拟币余额");
        check(data.userAvatarCostumes.Single().avatarCostumeId == 501 && data.userAvatarAccessories.Single().avatarAccessoryId == 502 &&
            data.userAvatarMotions.Single().avatarMotionId == 503 && data.userAvatar.avatarSkinColorId == 1 && data.userAvatar.avatarCostumeId == null,
            "购买后的独立Suite确认服装、配件和动作到账，当前穿戴不变");
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!;
        check(record["request"] == null && record["response"]!["userResources"]![0]!["resourceLevel"] == null &&
            record["response"]!["obtainedFirstPurchaseBonusList"]!.AsArray().Count == 0,
            "普通虚拟商品POST无body，响应省略零等级并保留空赠品列表");
        check(data.userVirtualShops[0].userVirtualShopItems.Single(i => i.virtualShopItemId == 4).status == "sold_out" &&
            data.userVirtualShops[0].userVirtualShopItems.All(i => i.buyCount == null),
            "同物品其他入口一并售罄，不虚构普通商品购买次数");
        check(Operations.Path(Operations.All["virtual-shop-purchase"], scenario.Steps[0], 1) ==
            $"/api/user/1/virtual-shop/{ShopId}/item/1?costResourceType=JEWEL", "虚拟商店消耗类型使用客户端的大写查询枚举");
        var before = DumpSerializer.Serialize(data);
        foreach (var entry in new[] { (Step: Buy(4, "JEWEL"), Status: 409, Error: ""),
            (Step: Buy(5, "JEWEL"), Status: 409, Error: "not_enough_resource"),
            (Step: Buy(5, "PAID_JEWEL"), Status: 409, Error: ""),
            (Step: Buy(999, "JEWEL"), Status: 404, Error: "") })
        {
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var failed = false;
            try { await invalid.Send(entry.Step); } catch (ClientFailure) { failed = true; }
            check(failed && invalid.LastHttpStatus == entry.Status && invalid.LastResponse?["errorCode"]?.GetValue<string>() == entry.Error &&
                invalid.LastResponse?["errorMessage"]?.GetValue<string>() == "", "重复购买、余额不足、未提供消耗和未知商品返回已核验错误结构");
        }
        check(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "虚拟商店HTTP拒绝不扣款、不发物品、不改库存");
        await client.Send(new() { Operation = "system" });
        var rejected = false;
        try { Operations.Path(Operations.All["virtual-shop-purchase"], Buy(1, "jewel"), 1); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected, "虚拟商店客户端拒绝非规范消耗枚举");
        store.Save(1, original);
    }
}
