using System.Text.Json.Nodes;
using MessagePack;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class RateChoiceGachaHttpChecks
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
            {"id":9880,"startAt":1,"gachaType":"ceil","rateChoiceGachaWishGroupId":9880,"wishSelectCount":10,"gachaCeilItemId":9880,
             "gachaDetails":[],"gachaCardRarityRates":[{"cardRarityType":"rarity_4","lotteryType":"normal","rate":0},
              {"cardRarityType":"rarity_4","lotteryType":"rate_choice_first","rate":0.8},
              {"cardRarityType":"rarity_4","lotteryType":"rate_choice_second","rate":1.6}],
             "gachaBehaviors":[{"id":9880,"spinCount":10,"gachaBehaviorType":"over_rarity_3_once","costResourceType":"jewel","costResourceQuantity":3000},
              {"id":9881,"spinCount":1,"costResourceType":"gacha_ticket","costResourceId":9880,"costResourceQuantity":1}]}
            """)!;
        for (var i = 1; i <= 11; i++)
        {
            pool["gachaDetails"]!.AsArray().Add(new JsonObject
                { ["id"] = 988000 + i, ["cardId"] = 98800 + i, ["weight"] = 1, ["isWish"] = i <= 10 });
            Add("cards", new JsonObject { ["id"] = 98800 + i, ["characterId"] = 1, ["cardRarityType"] = "rarity_4" });
        }
        Add("gachas", pool);
        Add("rateChoiceGachaWishes", JsonNode.Parse("""{"id":9880,"groupId":9880,"seq":1,"lotteryType":"rate_choice_first","selectCount":2}""")!);
        Add("rateChoiceGachaWishes", JsonNode.Parse("""{"id":9881,"groupId":9880,"seq":2,"lotteryType":"rate_choice_second","selectCount":8}""")!);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        void Reset()
        {
            var state = original.DeepClone();
            state.Data.userChargedCurrency = new() { free = 6000, paid = 1500 };
            state.Data.userCards = [];
            state.Data.userGachas = [];
            state.Data.userGachaCeilItems = [];
            state.Data.userGachaTickets = [new() { userId = 1, gachaTicketId = 9880, quantity = 5 }];
            state.Data.userRateChoiceGachaWishes = [];
            state.Data.refreshableTypes = [];
            store.Save(1, state);
        }
        ScenarioStep Select()
        {
            var details = new JsonArray();
            for (var i = 1; i <= 10; i++) details.Add(new JsonObject
                { ["rateChoiceGachaWishId"] = i <= 2 ? 9880 : 9881, ["gachaDetailId"] = 988000 + i });
            return new() { Operation = "gacha-wish", Body = new JsonObject { ["gachaId"] = 9880, ["rateChoiceGachaDetails"] = details } };
        }
        ScenarioStep Draw(int behavior = 9880) => new() { Operation = "gacha-draw", Args = new()
            { ["gachaId"] = "9880", ["gachaBehaviorId"] = behavior.ToString() } };
        Reset();
        var selected = await client.Send(Select());
        check(selected["updatedResources"]!["userRateChoiceGachaWishes"]!.AsArray().Count == 10
            && store.Read(1)!.Data.userCards.Length == 0 && store.Read(1)!.Data.userChargedCurrency.free == 6000,
            "现有Client选择操作通过真实加密HTTP一次注册两主八副，只保存名单不收费或发卡");
        var ten = await client.Send(Draw());
        var prizes = ten["obtainPrizes"]!.AsArray();
        check(prizes.Count == 10 && prizes.All(p =>
        {
            var card = p!["card"]!["resourceId"]!.GetValue<int>();
            return card is >= 98801 and <= 98810
                && p["gachaLotteryType"]!.GetValue<string>() == "normal";
        }), "HTTP结果实际使用主副名单，未选卡不占用两档名额，响应沿用普通奖品类型");
        var twice = Draw(9881);
        twice.Query["executeCount"] = "2";
        await ScenarioRunner.Run(client, new() { Steps = [twice] }, Path.Combine(directory, "rate-choice-gacha"));
        var data = store.Read(1)!.Data;
        check(data.userChargedCurrency.free == 3000 && data.userGachaTickets.Single().quantity == 3
            && data.userGachaCeilItems.Single().quantity == 12 && data.userGachas.Single(g => g.gachaBehaviorId == 9880).count == 1
            && data.userGachas.Single(g => g.gachaBehaviorId == 9881).count == 2
            && data.userCards.Sum(c => c.duplicateCount + 1) == 12 && data.userRateChoiceGachaWishes.Length == 10,
            "十连加指定两次票券单抽贯通费用、天井、计次、新卡重复卡和持久名单");
        var expected = JsonNode.Parse(MessagePackSerializer.ConvertToJson(DumpSerializer.Serialize(data)))!;
        foreach (var key in new[] { "userGachaTickets", "userGachaCeilItems" })
            foreach (var row in expected[key]!.AsArray()) row!.AsObject().Remove("userId");
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            var suite = await fresh.Suite();
            foreach (var key in new[] { "userRateChoiceGachaWishes", "userGachas", "userGachaCeilItems", "userGachaTickets", "userCards", "userChargedCurrency" })
                check(JsonNode.DeepEquals(suite[key], expected[key]), "独立认证Suite完整回读Rate Choice状态：" + key);
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await fresh.Send(Select()); }
            catch (ClientFailure) { }
            check(fresh.LastHttpStatus == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "已注册名单即使重复提交相同内容也拒绝，旧名单和库存保持不变");
        }
        foreach (var kind in new[] { "missing", "partial", "duplicate", "not-wish", "ordinary" })
        {
            Reset();
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var step = Select();
            var rows = step.Body!["rateChoiceGachaDetails"]!.AsArray();
            if (kind == "partial") rows.RemoveAt(9);
            if (kind == "duplicate") rows[2]!["gachaDetailId"] = 988001;
            if (kind == "not-wish") rows[2]!["gachaDetailId"] = 988011;
            if (kind == "ordinary") step.Body["gachaId"] = 1;
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await invalid.Send(kind == "missing" ? Draw() : step); }
            catch (ClientFailure) { }
            check(invalid.LastHttpStatus == (kind == "missing" ? 409 : 400)
                && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "缺名单、残缺、重复、不合资格及普通卡池伪选择的HTTP拒绝与整体回滚：" + kind);
        }
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }
}
