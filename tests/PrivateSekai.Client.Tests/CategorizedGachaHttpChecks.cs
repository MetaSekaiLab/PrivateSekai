using System.Text.Json.Nodes;
using MessagePack;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class CategorizedGachaHttpChecks
{
    public static void WriteMaster(string directory)
    {
        void Add(string table, JsonNode row)
        {
            var path = Path.Combine(directory, table + ".json");
            var rows = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsArray() : new JsonArray();
            rows.Add(row);
            JsonFiles.Write(path, rows);
        }
        for (var card = 1; card <= 20; card++) Add("cards", new JsonObject
            { ["id"] = 99200 + card, ["characterId"] = 1, ["cardRarityType"] = "rarity_4" });
        foreach (var id in new[] { 9920, 9921 })
        {
            var pool = JsonNode.Parse("""
                {"startAt":1,"wishFixedSelectCount":6,"gachaDetails":[],
                 "gachaCardRarityRates":[{"cardRarityType":"rarity_4","lotteryType":"normal","rate":0},
                  {"cardRarityType":"rarity_4","lotteryType":"categorized_wish","rate":100}],"gachaBehaviors":[]}
                """)!;
            pool["id"] = id;
            pool["gachaCeilItemId"] = id;
            pool["wishSelectCount"] = id == 9920 ? 10 : 8;
            pool["wishLimitedSelectCount"] = id == 9920 ? 1 : 2;
            pool["gachaBehaviors"]!.AsArray().Add(new JsonObject { ["id"] = id, ["spinCount"] = 10,
                ["executeLimit"] = 1, ["gachaBehaviorType"] = "over_rarity_3_once", ["costResourceType"] = id == 9920 ? "jewel" : "paid_jewel",
                ["costResourceQuantity"] = id == 9920 ? 3000 : 1500 });
            pool["gachaBehaviors"]!.AsArray().Add(new JsonObject { ["id"] = id + 100, ["spinCount"] = 1,
                ["costResourceType"] = "gacha_ticket", ["costResourceId"] = 9920, ["costResourceQuantity"] = 1 });
            for (var card = 1; card <= 20; card++) pool["gachaDetails"]!.AsArray().Add(new JsonObject
            {
                ["id"] = id * 100 + card, ["cardId"] = 99200 + card, ["weight"] = 1, ["isWish"] = card < 20,
                ["gachaDetailWishType"] = card <= 6 ? "fixed" : card <= 9 ? "limited" : "normal"
            });
            Add("gachas", pool);
        }
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        void Reset()
        {
            var state = original.DeepClone();
            state.Data.userChargedCurrency = new() { free = 10000, paid = 5000 };
            state.Data.userCards = [];
            state.Data.userGachas = [];
            state.Data.userGachaCeilItems = [];
            state.Data.userGachaTickets = [new() { userId = 1, gachaTicketId = 9920, quantity = 10 }];
            state.Data.userCategorizedGachaWishes = [];
            state.Data.refreshableTypes = [];
            store.Save(1, state);
        }
        ScenarioStep Select(int id = 9920)
        {
            JsonArray Ids(IEnumerable<int> ids) => new(ids.Select(i => (JsonNode?)JsonValue.Create(id * 100 + i)).ToArray());
            return new() { Operation = "gacha-categorized-wish", Body = new JsonObject
            {
                ["gachaId"] = id, ["gachaDetailFixedIds"] = Ids(Enumerable.Range(1, 6)),
                ["gachaDetailLimitedIds"] = Ids(id == 9920 ? [7] : [13, 14]),
                ["gachaDetailNormalIds"] = Ids(id == 9920 ? [10, 11, 12] : [])
            } };
        }
        ScenarioStep Draw(int id = 9920, bool ticket = false) => new() { Operation = "gacha-draw", Args = new()
            { ["gachaId"] = id.ToString(), ["gachaBehaviorId"] = (ticket ? id + 100 : id).ToString() } };
        Reset();
        var selected = await client.Send(Select());
        check(selected["updatedResources"]!["userCategorizedGachaWishes"]!.AsArray().Count == 10
            && store.Read(1)!.Data.userCards.Length == 0 && store.Read(1)!.Data.userChargedCurrency.free == 10000,
            "原始三数组模型通过加密HTTP一次注册6固定1特别3普通，不发卡收费");
        await client.Send(Select(9921));
        var wishes = store.Read(1)!.Data.userCategorizedGachaWishes;
        check(wishes.Length == 18 && wishes.Where(w => w.gachaId == 9921).Count(w => w.categorizedWishType == "limited") == 2
            && wishes.Single(w => w.gachaDetailId == 992113).categorizedWishType == "limited",
            "另一池6固定2特别0普通可独立注册，普通成员按特别槽位持久化");
        foreach (var id in new[] { 9920, 9921 })
        {
            var drawn = await client.Send(Draw(id));
            var allowed = wishes.Where(w => w.gachaId == id).Select(w => 99200 + w.gachaDetailId % 100).ToHashSet();
            check(drawn["obtainPrizes"]!.AsArray().Count == 10 && drawn["obtainPrizes"]!.AsArray().All(p =>
                allowed.Contains(p!["card"]!["resourceId"]!.GetValue<int>()) && p["gachaLotteryType"]!.GetValue<string>() == "normal"),
                "HTTP实际抽取使用本池完整分类名单，特别槽位普通成员也可出，响应保留normal");
        }
        var multiple = Draw(ticket: true);
        multiple.Query["executeCount"] = "3";
        await ScenarioRunner.Run(client, new() { Steps = [multiple] }, Path.Combine(directory, "categorized-gacha"));
        var data = store.Read(1)!.Data;
        check(data.userChargedCurrency.free == 7000 && data.userChargedCurrency.paid == 3500 && data.userGachaTickets.Single().quantity == 7
            && data.userGachas.Sum(g => g.count) == 5 && data.userCards.Sum(c => c.duplicateCount + 1) == 23
            && data.userGachaCeilItems.Sum(i => i.quantity) == 23 && data.userCategorizedGachaWishes.Length == 18,
            "两个池的付费、普通水晶、票券多抽贯通费用、23张结果、天井、计次和持久名单");
        var expected = JsonNode.Parse(MessagePackSerializer.ConvertToJson(DumpSerializer.Serialize(data)))!;
        foreach (var key in new[] { "userGachaCeilItems", "userGachaTickets" })
            foreach (var row in expected[key]!.AsArray()) row!.AsObject().Remove("userId");
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            var suite = await fresh.Suite();
            foreach (var key in new[] { "userCategorizedGachaWishes", "userGachas", "userGachaCeilItems", "userGachaTickets", "userCards", "userChargedCurrency" })
                check(JsonNode.DeepEquals(suite[key], expected[key]), "独立认证Suite完整回读分类Wish状态：" + key);
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await fresh.Send(Select()); }
            catch (ClientFailure) { }
            check(fresh.LastHttpStatus == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "抽取后注册重复名单仍拒绝，库存和旧名单不变");
        }
        foreach (var kind in new[] { "missing", "partial", "wrong-fixed", "limited-in-normal", "duplicate", "ordinary", "rate-choice" })
        {
            Reset();
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var step = Select();
            if (kind == "partial") step.Body!["gachaDetailNormalIds"]!.AsArray().RemoveAt(2);
            if (kind == "wrong-fixed") step.Body!["gachaDetailFixedIds"]![0] = 992008;
            if (kind == "limited-in-normal") step.Body!["gachaDetailNormalIds"]![0] = 992008;
            if (kind == "duplicate") step.Body!["gachaDetailLimitedIds"]![0] = 992010;
            if (kind == "ordinary") step.Body!["gachaId"] = 1;
            if (kind == "rate-choice") step.Body!["gachaId"] = 9880;
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await invalid.Send(kind == "missing" ? Draw() : step); }
            catch (ClientFailure) { }
            check(invalid.LastHttpStatus == (kind == "missing" ? 409 : 400)
                && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "非法分类选择或缺名单的HTTP拒绝与整体回滚：" + kind);
        }
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }
}
