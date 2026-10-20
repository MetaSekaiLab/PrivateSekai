using System.Text.Json.Nodes;
using MessagePack;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class SelectListGachaHttpChecks
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
        var pool = JsonNode.Parse("""
            {"id":9890,"startAt":1,"wishSelectCount":10,"gachaCeilItemId":9890,"gachaDetails":[],
             "gachaCardRarityRates":[{"cardRarityType":"rarity_4","lotteryType":"normal","rate":100}],
             "gachaBehaviors":[{"id":9890,"spinCount":10,"executeLimit":3,"gachaBehaviorType":"over_rarity_3_once","costResourceType":"jewel","costResourceQuantity":3000}]}
            """)!;
        for (var i = 1; i <= 12; i++)
        {
            pool["gachaDetails"]!.AsArray().Add(new JsonObject
                { ["id"] = 989000 + i, ["cardId"] = 98900 + i, ["weight"] = 1, ["isWish"] = i <= 11 });
            Add("cards", new JsonObject { ["id"] = 98900 + i, ["characterId"] = 1, ["cardRarityType"] = "rarity_4" });
        }
        Add("gachas", pool);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        void Reset()
        {
            var state = original.DeepClone();
            state.Data.userChargedCurrency = new() { free = 9000, paid = 1500 };
            state.Data.userCards = [];
            state.Data.userGachas = [];
            state.Data.userGachaCeilItems = [];
            state.Data.userGachaWishes = [new() { gachaId = 9999, gachaDetailId = 99990 }];
            state.Data.refreshableTypes = [];
            store.Save(1, state);
        }
        ScenarioStep Select(int start = 1) => new() { Operation = "gacha-select-wish", Body = new JsonObject
            { ["gachaId"] = 9890, ["gachaDetailIds"] = new JsonArray(Enumerable.Range(start, 10).Select(i => (JsonNode?)JsonValue.Create(989000 + i)).ToArray()) } };
        ScenarioStep Draw() => new() { Operation = "gacha-draw", Args = new() { ["gachaId"] = "9890", ["gachaBehaviorId"] = "9890" } };
        Reset();
        var saved = await client.Send(Select());
        check(saved["updatedResources"]!["userGachaWishes"]!.AsArray().Count == 11 && store.Read(1)!.Data.userChargedCurrency.free == 9000
            && store.Read(1)!.Data.userCards.Length == 0, "普通Wish经原模型和真实加密HTTP保存完整名单，保留其他池，不扣费或发卡");
        var first = await client.Send(Draw());
        check(first["obtainPrizes"]!.AsArray().Count == 10 && first["obtainPrizes"]!.AsArray().All(p =>
            p!["card"]!["resourceId"]!.GetValue<int>() is >= 98901 and <= 98910), "第一次十连实际排除未选四星");
        var changed = await client.Send(Select(2));
        check(changed["updatedResources"]!["userGachaWishes"]!.AsArray().Count == 11
            && store.Read(1)!.Data.userGachaWishes.Where(w => w.gachaId == 9890).Select(w => w.gachaDetailId).SequenceEqual(Enumerable.Range(989002, 10)),
            "抽过后可改选，完整替换本池名单且保留提交顺序和其他池");
        var second = await client.Send(Draw());
        check(second["obtainPrizes"]!.AsArray().All(p => p!["card"]!["resourceId"]!.GetValue<int>() is >= 98902 and <= 98911),
            "改选后的十连使用新名单，原名单移出成员不再可出");
        await ScenarioRunner.Run(client, new() { Steps = [Draw()] }, Path.Combine(directory, "select-list-gacha"));
        var data = store.Read(1)!.Data;
        check(data.userChargedCurrency.free == 0 && data.userChargedCurrency.paid == 1500 && data.userGachas.Single().count == 3
            && data.userCards.Sum(c => c.duplicateCount + 1) == 30 && data.userGachaCeilItems.Single().quantity == 30,
            "三次十连贯通免费水晶优先、行为次数、天井和新卡重复卡结算");
        var expected = JsonNode.Parse(MessagePackSerializer.ConvertToJson(DumpSerializer.Serialize(data)))!;
        foreach (var row in expected["userGachaCeilItems"]!.AsArray()) row!.AsObject().Remove("userId");
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            var suite = await fresh.Suite();
            foreach (var key in new[] { "userGachaWishes", "userGachas", "userGachaCeilItems", "userCards", "userChargedCurrency" })
                check(JsonNode.DeepEquals(suite[key], expected[key]), "独立认证Suite完整回读普通Wish结算状态：" + key);
            foreach (var step in new[] { Select(), Draw() })
            {
                using var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
                await rejected.Send(new() { Operation = "system" });
                var before = DumpSerializer.Serialize(store.Read(1)!.Data);
                try { await rejected.Send(step); }
                catch (ClientFailure) { }
                check(rejected.LastHttpStatus == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                    "次数耗尽后改选与抽取均拒绝，原名单和库存完全保留");
            }
        }
        foreach (var kind in new[] { "missing", "partial", "duplicate", "card-id", "not-wish", "ordinary", "rate-choice" })
        {
            Reset();
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var step = Select();
            var ids = step.Body!["gachaDetailIds"]!.AsArray();
            if (kind == "partial") ids.RemoveAt(9);
            if (kind == "duplicate") ids[1] = 989001;
            if (kind == "card-id") ids[1] = 98902;
            if (kind == "not-wish") ids[1] = 989012;
            if (kind == "ordinary") step.Body["gachaId"] = 1;
            if (kind == "rate-choice") step.Body["gachaId"] = 9880;
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await invalid.Send(kind == "missing" ? Draw() : step); }
            catch (ClientFailure) { }
            check(invalid.LastHttpStatus == (kind == "missing" ? 409 : 400)
                && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "缺名单或非法普通Wish的HTTP拒绝与整体回滚：" + kind);
        }
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }
}
