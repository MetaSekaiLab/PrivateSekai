using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class ShopHttpChecks
{
    public static void WriteMaster(string directory)
    {
        // 隔离测试商品，不代表官方商店配置。
        File.WriteAllText(Path.Combine(directory, "shopItems.json"), """
            [{"id":1,"shopId":1,"resourceBoxId":21,"costs":[{"cost":{"resourceType":"coin","quantity":30}}]},
             {"id":2,"shopId":1,"resourceBoxId":21,"costs":[{"cost":{"resourceType":"coin","quantity":30}}]},
             {"id":3,"shopId":2,"resourceBoxId":22,"costs":[{"cost":{"resourceType":"material","resourceId":13,"quantity":10}}]}]
            """);
        var path = Path.Combine(directory, "resourceBoxes.json");
        var boxes = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        boxes.Add(JsonNode.Parse("""{"id":21,"resourceBoxPurpose":"shop_item","details":[{"resourceType":"material","resourceId":1,"resourceQuantity":4}]}"""));
        boxes.Add(JsonNode.Parse("""{"id":22,"resourceBoxPurpose":"shop_item","details":[{"resourceType":"music","resourceId":6,"resourceQuantity":1}]}"""));
        JsonFiles.Write(path, boxes);
        File.WriteAllText(Path.Combine(directory, "musicVocals.json"),
            """[{"id":7,"musicId":6,"releaseConditionId":5},{"id":8,"musicId":6,"releaseConditionId":5},{"id":9,"musicId":6,"releaseConditionId":99}]""");
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userGamedata.coin = 100;
        state.Data.userMaterials = [];
        state.Data.userShops = [];
        store.Save(1, state);
        var scenario = new Scenario { Steps =
        [
            new() { Operation = "shop-purchase", Args = new() { ["shopId"] = "1", ["shopItemId"] = "1" },
                Expect = new() { ["/updatedResources/userGamedata/coin"] = JsonValue.Create(70) } },
            new() { Operation = "shop-upgrade", Args = new() { ["shopId"] = "1", ["shopItemId"] = "2" } }
        ] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "shop"));
        var saved = store.Read(1)!.Data;
        check(saved.userGamedata.coin == 40 && saved.userMaterials.Single().quantity == 8,
            "现有商店 HTTP 接口按测试 master 扣费并发奖，未将 PUT 视为区域升级已核验");
        check(saved.userShops.Single().userShopItems.Length == 2, "商店状态通过真实控制器保存");
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "shop/001.json")))!;
        check(record["status"]!.GetValue<string>() == "completed" && record["stateChanges"]!.AsArray().Count > 0,
            "购买操作保存前后资源变化");
        var left = JsonNode.Parse("""{"userShops":[{"shopId":1,"userShopItems":[{"shopItemId":2},{"shopItemId":1}]}]}""");
        var right = JsonNode.Parse("""{"userShops":[{"shopId":1,"userShopItems":[{"shopItemId":1},{"shopItemId":2}]}]}""");
        check(Comparison.Diff(Comparison.Normalize(left), Comparison.Normalize(right)).Count == 0,
            "商店及商品按业务 ID 对齐，避免顺序变化导致误报");
        state = store.Read(1)!;
        state.Data.userMaterials = [new() { materialId = 13, quantity = 10 }];
        state.Data.userMusics = [];
        state.Data.userMusicVocals = [];
        state.Data.userShops = [new() { shopId = 2, userShopItems = [new() { shopItemId = 3, status = "sale" }] }];
        store.Save(1, state);
        await ScenarioRunner.Run(client, new() { Steps = [new()
        {
            Operation = "shop-purchase", Args = new() { ["shopId"] = "2", ["shopItemId"] = "3" }
        }] }, Path.Combine(directory, "music-shop"));
        saved = store.Read(1)!.Data;
        check(saved.userMaterials.Single().quantity == 0 && saved.userMusics.Single().musicId == 6 &&
            saved.userMusicVocals.Select(v => v.musicVocalId).SequenceEqual(new[] { 7, 8 }) &&
            saved.userShops.Single().userShopItems.Single().status == "sold_out",
            "音乐购买扣除音乐卡、解锁歌曲及默认音源并标记售罄");
        var musicResponse = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "music-shop/001.json")))!["response"]!["updatedResources"]!;
        check(musicResponse["userShops"]![0]!["userShopItems"]![0]!["level"] == null,
            "无等级的音乐商品省略 level 字段");
    }
}
